namespace Domain.Demands;

/// <param name="DemandId">Primary key of <c>dbo.Demand</c>.</param>
/// <param name="SirenNumber">Company identifier; not unique across demands.</param>
/// <param name="BaeLink">Stored reference of the PDF (an absolute HTTPS SharePoint URL).</param>
/// <param name="StatusCode">Code of <c>dbo.Status</c>: <c>pending</c>, <c>ok</c> or <c>fail</c>.</param>
public sealed record Demand(int DemandId, SirenNumber SirenNumber, string BaeLink, string StatusCode);
