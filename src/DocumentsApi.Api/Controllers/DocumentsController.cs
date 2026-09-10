using System.Security.Cryptography;
using DocumentsApi.Api.Data.Entities;
using DocumentsApi.Api.Dtos;
using DocumentsApi.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DocumentsApi.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB

    private readonly IPdfSigningService _pdfSigningService;
    private readonly IDocumentSignatureRepository _signatureRepository;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IPdfSigningService pdfSigningService,
        IDocumentSignatureRepository signatureRepository,
        ILogger<DocumentsController> logger)
    {
        _pdfSigningService = pdfSigningService;
        _signatureRepository = signatureRepository;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a PDF, stamps it with the given username and the current date/time,
    /// records the signing event, and returns the signed PDF for download.
    /// </summary>
    [HttpPost("sign")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sign([FromForm] SignDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request.File.Length == 0)
        {
            return BadRequest("The uploaded file is empty.");
        }

        if (request.File.Length > MaxFileSizeBytes)
        {
            return BadRequest($"The uploaded file exceeds the maximum allowed size of {MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        var isPdf = string.Equals(request.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(request.File.FileName), ".pdf", StringComparison.OrdinalIgnoreCase);

        if (!isPdf)
        {
            return BadRequest("Only PDF files are supported.");
        }

        byte[] sourceBytes;
        using (var memoryStream = new MemoryStream())
        {
            await request.File.CopyToAsync(memoryStream, cancellationToken);
            sourceBytes = memoryStream.ToArray();
        }

        var signedAtUtc = DateTime.UtcNow;
        byte[] signedBytes;
        try
        {
            signedBytes = _pdfSigningService.Sign(sourceBytes, request.Username, signedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sign uploaded PDF {FileName}", request.File.FileName);
            return BadRequest("The uploaded file could not be processed as a PDF.");
        }

        var documentHash = Convert.ToHexString(SHA256.HashData(signedBytes));

        var signature = await _signatureRepository.AddAsync(new DocumentSignature
        {
            FileName = request.File.FileName,
            SignedBy = request.Username,
            SignedAtUtc = signedAtUtc,
            DocumentHash = documentHash,
        }, cancellationToken);

        Response.Headers["X-Signature-Id"] = signature.Id.ToString();

        var signedFileName = $"{Path.GetFileNameWithoutExtension(request.File.FileName)}-signed.pdf";
        return File(signedBytes, "application/pdf", signedFileName);
    }
}
