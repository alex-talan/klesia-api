using System.Globalization;
using System.Text.Json.Nodes;
using Xunit.Sdk;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace IntegrationTests.Contract;

internal sealed class ContractDocument
{
    private readonly JsonObject _root;

    private ContractDocument(JsonObject root)
    {
        _root = root;
    }

    public static ContractDocument LoadOrSkip(string? startDirectory = null)
    {
        var contractPath = FindContractFile(startDirectory ?? AppContext.BaseDirectory);
        if (contractPath is null)
        {
            throw SkipException.ForSkip(
                "Contract assertions skipped: contracts/openapi.yaml was not found while walking up from the test assembly directory.");
        }

        using var reader = File.OpenText(contractPath);
        var yaml = new YamlStream();
        yaml.Load(reader);
        var root = ConvertNode(yaml.Documents.Single().RootNode) as JsonObject
            ?? throw new InvalidDataException("The OpenAPI contract root must be a YAML mapping.");

        return new ContractDocument(root);
    }

    internal static string? FindContractFile(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "contracts", "openapi.yaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    internal JsonObject GetResponse(string operationId, string statusCode)
    {
        var operation = FindOperation(operationId);
        var responses = operation["responses"] as JsonObject
            ?? throw new InvalidDataException($"Operation '{operationId}' has no responses object.");
        var response = responses[statusCode] ?? responses["default"]
            ?? throw new InvalidDataException($"Operation '{operationId}' does not document response '{statusCode}'.");

        return ResolveReferences(response, new HashSet<string>()) as JsonObject
            ?? throw new InvalidDataException($"Response '{statusCode}' for '{operationId}' must be an object.");
    }

    internal JsonNode ResolveSchema(JsonNode schema) => ResolveReferences(schema, new HashSet<string>());

    internal IEnumerable<ContractResponseExample> GetResponseExamples()
    {
        var paths = _root["paths"] as JsonObject
            ?? throw new InvalidDataException("The OpenAPI contract has no paths object.");

        foreach (var path in paths)
        {
            if (path.Value is not JsonObject pathItem)
            {
                continue;
            }

            foreach (var method in pathItem)
            {
                if (method.Value is not JsonObject operation || operation["operationId"]?.GetValue<string>() is not { } operationId)
                {
                    continue;
                }

                if (operation["responses"] is not JsonObject responses)
                {
                    continue;
                }

                foreach (var responseEntry in responses)
                {
                    if (responseEntry.Value is null)
                    {
                        continue;
                    }

                    var response = ResolveReferences(responseEntry.Value, new HashSet<string>()) as JsonObject;
                    if (response?["content"] is not JsonObject content)
                    {
                        continue;
                    }

                    foreach (var mediaEntry in content)
                    {
                        if (mediaEntry.Value is not JsonObject media || media["schema"] is not JsonNode schema)
                        {
                            continue;
                        }

                        var resolvedSchema = ResolveSchema(schema);
                        if (media["example"] is JsonNode example)
                        {
                            yield return new ContractResponseExample(
                                operationId,
                                responseEntry.Key,
                                mediaEntry.Key,
                                example.DeepClone(),
                                resolvedSchema.DeepClone());
                        }

                        if (media["examples"] is not JsonObject examples)
                        {
                            continue;
                        }

                        foreach (var exampleEntry in examples)
                        {
                            if (exampleEntry.Value is JsonObject exampleObject && exampleObject["value"] is JsonNode exampleValue)
                            {
                                yield return new ContractResponseExample(
                                    operationId,
                                    responseEntry.Key,
                                    mediaEntry.Key,
                                    exampleValue.DeepClone(),
                                    resolvedSchema.DeepClone());
                            }
                        }
                    }
                }
            }
        }
    }

    internal IReadOnlyList<string> Validate(JsonNode schema, JsonNode? instance)
    {
        return ContractValidator.ValidateJson(ResolveSchema(schema), instance);
    }

    private JsonObject FindOperation(string operationId)
    {
        var paths = _root["paths"] as JsonObject
            ?? throw new InvalidDataException("The OpenAPI contract has no paths object.");

        foreach (var path in paths)
        {
            if (path.Value is not JsonObject pathItem)
            {
                continue;
            }

            foreach (var method in pathItem)
            {
                if (method.Value is JsonObject operation && operation["operationId"]?.GetValue<string>() == operationId)
                {
                    return operation;
                }
            }
        }

        throw new InvalidDataException($"Operation '{operationId}' was not found in the OpenAPI contract.");
    }

    private JsonNode ResolveReferences(JsonNode node, HashSet<string> referencesBeingResolved)
    {
        if (node is JsonObject objectNode)
        {
            if (objectNode.TryGetPropertyValue("$ref", out var referenceNode))
            {
                var reference = referenceNode?.GetValue<string>()
                    ?? throw new InvalidDataException("An OpenAPI $ref must be a string.");
                if (!referencesBeingResolved.Add(reference))
                {
                    throw new InvalidDataException($"Circular OpenAPI reference '{reference}' is not supported by this test helper.");
                }

                try
                {
                    var target = ResolveReferences(ResolvePointer(reference), referencesBeingResolved);
                    if (objectNode.Count == 1)
                    {
                        return target.DeepClone();
                    }

                    if (target is not JsonObject targetObject)
                    {
                        throw new InvalidDataException($"OpenAPI reference '{reference}' with sibling properties must target an object.");
                    }

                    var merged = (JsonObject)targetObject.DeepClone();
                    foreach (var sibling in objectNode.Where(property => property.Key != "$ref"))
                    {
                        merged[sibling.Key] = sibling.Value is null
                            ? null
                            : ResolveReferences(sibling.Value, referencesBeingResolved);
                    }

                    return merged;
                }
                finally
                {
                    referencesBeingResolved.Remove(reference);
                }
            }

            var resolvedObject = new JsonObject();
            foreach (var property in objectNode)
            {
                resolvedObject[property.Key] = property.Value is null
                    ? null
                    : ResolveReferences(property.Value, referencesBeingResolved);
            }

            return resolvedObject;
        }

        if (node is JsonArray arrayNode)
        {
            var resolvedArray = new JsonArray();
            foreach (var item in arrayNode)
            {
                resolvedArray.Add(item is null ? null : ResolveReferences(item, referencesBeingResolved));
            }

            return resolvedArray;
        }

        return node.DeepClone();
    }

    private JsonNode ResolvePointer(string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"External OpenAPI reference '{reference}' is not supported.");
        }

        JsonNode current = _root;
        foreach (var encodedToken in reference[2..].Split('/'))
        {
            var token = encodedToken.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            current = current switch
            {
                JsonObject objectNode when objectNode.TryGetPropertyValue(token, out var value) && value is not null => value,
                JsonArray arrayNode when int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                    && index >= 0 && index < arrayNode.Count && arrayNode[index] is { } value => value,
                _ => throw new InvalidDataException($"OpenAPI reference '{reference}' could not be resolved."),
            };
        }

        return current;
    }

    private static JsonNode? ConvertNode(YamlNode node) => node switch
    {
        YamlScalarNode scalar => ConvertScalar(scalar),
        YamlSequenceNode sequence => ConvertSequence(sequence),
        YamlMappingNode mapping => ConvertMapping(mapping),
        _ => throw new InvalidDataException($"Unsupported YAML node type '{node.GetType().Name}'."),
    };

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (value is null)
        {
            return null;
        }

        if (scalar.Style == ScalarStyle.Plain)
        {
            if (value is "null" or "Null" or "NULL" or "~")
            {
                return null;
            }

            if (bool.TryParse(value, out var boolean))
            {
                return JsonValue.Create(boolean);
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                return JsonValue.Create(integer);
            }

            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return JsonValue.Create(number);
            }
        }

        return JsonValue.Create(value);
    }

    private static JsonArray ConvertSequence(YamlSequenceNode sequence)
    {
        var result = new JsonArray();
        foreach (var item in sequence.Children)
        {
            result.Add(ConvertNode(item));
        }

        return result;
    }

    private static JsonObject ConvertMapping(YamlMappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var item in mapping.Children)
        {
            if (item.Key is not YamlScalarNode key || key.Value is null)
            {
                throw new InvalidDataException("OpenAPI YAML mapping keys must be strings.");
            }

            result[key.Value] = ConvertNode(item.Value);
        }

        return result;
    }
}

internal sealed record ContractResponseExample(
    string OperationId,
    string StatusCode,
    string MediaType,
    JsonNode Value,
    JsonNode Schema);