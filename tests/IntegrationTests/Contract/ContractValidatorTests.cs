using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Xunit.Sdk;

namespace IntegrationTests.Contract;

public sealed class ContractValidatorTests
{
    [Fact]
    public void EveryJsonResponseExample_ValidatesAgainstItsDeclaredSchema()
    {
        var contract = ContractDocument.LoadOrSkip();
        var examples = contract.GetResponseExamples()
            .Where(example => ContractValidator.IsJsonMediaType(example.MediaType))
            .ToList();

        examples.Should().NotBeEmpty();
        foreach (var example in examples)
        {
            contract.Validate(example.Schema, example.Value)
                .Should()
                .BeEmpty($"{example.OperationId} response {example.StatusCode} ({example.MediaType}) example should satisfy its schema");
        }
    }

    [Fact]
    public void DemandSchema_RejectsMutatedContractExample()
    {
        var contract = ContractDocument.LoadOrSkip();
        var example = contract.GetResponseExamples().First(item =>
            item.OperationId == "createDemand"
            && item.StatusCode == "201"
            && item.MediaType == "application/json");
        var mutations = new List<(string Name, System.Text.Json.Nodes.JsonObject Value)>();

        var missingField = (System.Text.Json.Nodes.JsonObject)example.Value.DeepClone();
        missingField.Remove("gk_number");
        mutations.Add(("missing gk_number", missingField));

        var wrongStatus = (System.Text.Json.Nodes.JsonObject)example.Value.DeepClone();
        wrongStatus["status"] = "done";
        mutations.Add(("unknown status", wrongStatus));

        var invalidId = (System.Text.Json.Nodes.JsonObject)example.Value.DeepClone();
        invalidId["demand_id"] = 0;
        mutations.Add(("zero demand_id", invalidId));

        var extraProperty = (System.Text.Json.Nodes.JsonObject)example.Value.DeepClone();
        extraProperty["unexpected"] = true;
        mutations.Add(("additional property", extraProperty));

        foreach (var mutation in mutations)
        {
            contract.Validate(example.Schema, mutation.Value)
                .Should()
                .NotBeEmpty($"the mutation '{mutation.Name}' should fail validation");
        }
    }

    [Fact]
    public void AssertResponse_ValidatesDocumentedOperationStatusAndContentType()
    {
        var contract = ContractDocument.LoadOrSkip();
        var examplesByOperation = contract.GetResponseExamples()
            .Where(example => ContractValidator.IsJsonMediaType(example.MediaType))
            .GroupBy(example => example.OperationId)
            .ToDictionary(group => group.Key, group => group.First());
        var expectedOperations = new[]
        {
            "getHealth",
            "listDemands",
            "getDemandDocument",
            "createDemand",
            "negotiateNotifications",
        };

        foreach (var operationId in expectedOperations.Where(id => id != "getDemandDocument"))
        {
            var example = examplesByOperation[operationId];
            using var response = new HttpResponseMessage((HttpStatusCode)int.Parse(example.StatusCode, System.Globalization.CultureInfo.InvariantCulture))
            {
                Content = new StringContent(example.Value.ToJsonString(), Encoding.UTF8, example.MediaType),
            };

            ContractValidator.AssertResponse(response, operationId);
        }

        using var documentResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        documentResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        ContractValidator.AssertResponse(documentResponse, "getDemandDocument");
    }

    [Fact]
    public void MissingContract_ProducesAnExplicitSkipReason()
    {
        var temporaryDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var exception = Record.Exception(() => ContractDocument.LoadOrSkip(temporaryDirectory.FullName));

            exception.Should().BeOfType<SkipException>();
            exception!.Message.Should().Contain("Contract assertions skipped").And.Contain("contracts/openapi.yaml");
        }
        finally
        {
            temporaryDirectory.Delete(true);
        }
    }
}