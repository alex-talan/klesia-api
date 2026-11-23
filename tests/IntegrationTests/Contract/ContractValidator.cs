using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace IntegrationTests.Contract;

internal static class ContractValidator
{
    public static void AssertResponse(HttpResponseMessage response, string operationId)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var document = ContractDocument.LoadOrSkip();
        var statusCode = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var responseDefinition = document.GetResponse(operationId, statusCode);
        var contentDefinitions = responseDefinition["content"] as JsonObject;
        var actualMediaType = response.Content.Headers.ContentType?.MediaType;

        if (contentDefinitions is null)
        {
            Xunit.Assert.True(
                actualMediaType is null,
                $"Operation '{operationId}' response {statusCode} does not document a response body, but received '{actualMediaType}'.");
            return;
        }

        Xunit.Assert.True(
            !string.IsNullOrWhiteSpace(actualMediaType),
            $"Operation '{operationId}' response {statusCode} must have a documented Content-Type.");

        var mediaDefinition = contentDefinitions.FirstOrDefault(
            item => string.Equals(item.Key, actualMediaType, StringComparison.OrdinalIgnoreCase));
        Xunit.Assert.True(
            mediaDefinition.Value is JsonObject,
            $"Operation '{operationId}' response {statusCode} does not document Content-Type '{actualMediaType}'.");

        if (!IsJsonMediaType(actualMediaType!))
        {
            return;
        }

        var schema = mediaDefinition.Value!["schema"]
            ?? throw new InvalidDataException($"Operation '{operationId}' response {statusCode} has no schema for '{actualMediaType}'.");
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        JsonNode? instance;
        try
        {
            instance = JsonNode.Parse(body);
        }
        catch (JsonException exception)
        {
            Xunit.Assert.Fail($"Operation '{operationId}' response {statusCode} body is not valid JSON: {exception.Message}");
            return;
        }

        var errors = ValidateJson(document.ResolveSchema(schema), instance);
        Xunit.Assert.True(
            errors.Count == 0,
            $"Operation '{operationId}' response {statusCode} does not satisfy its JSON Schema:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    internal static IReadOnlyList<string> ValidateJson(JsonNode schema, JsonNode? instance)
    {
        var jsonSchema = JsonSchema.FromText(schema.ToJsonString());
        var jsonInstance = JsonSerializer.SerializeToElement(instance);
        var evaluation = jsonSchema.Evaluate(jsonInstance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = new List<string>();
        CollectErrors(evaluation, errors);
        if (!evaluation.IsValid && errors.Count == 0)
        {
            errors.Add("The schema evaluation failed without a detailed error message.");
        }

        return errors;
    }

    internal static bool IsJsonMediaType(string mediaType) =>
        string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

    private static void CollectErrors(EvaluationResults results, ICollection<string> errors)
    {
        if (!results.IsValid && results.Errors is { Count: > 0 } validationErrors)
        {
            foreach (var error in validationErrors)
            {
                errors.Add($"{results.InstanceLocation}: {error.Value}");
            }
        }

        if (results.Details is not null)
        {
            foreach (var detail in results.Details)
            {
                CollectErrors(detail, errors);
            }
        }
    }
}