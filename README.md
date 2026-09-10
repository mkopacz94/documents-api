# Documents API

ASP.NET Core Web API for signing PDF documents. A caller uploads a PDF and a
username; the API stamps the username and the signing date/time onto the
document, records the signing event in a MySQL database via EF Core, and
returns the signed PDF so the caller can save it.

## How it works

1. `POST /api/documents/sign` accepts a `multipart/form-data` request with:
   - `file` — the PDF to sign
   - `username` — the signer's name
2. The API validates the upload (non-empty, PDF content type/extension, size limit).
3. `PdfSigningService` stamps `Signed by <username> on <UTC timestamp>` onto
   the document (bottom-right of the last page by default) using
   [PdfSharpCore](https://github.com/ststeiger/PdfSharpCore), with a font
   embedded in the assembly so the output doesn't depend on fonts installed
   on the host.
4. A `DocumentSignature` row (file name, signer, UTC timestamp, SHA-256 hash
   of the signed PDF) is written to MySQL via EF Core.
5. The signed PDF bytes are returned as `application/pdf` for the caller to
   download/save. The new signature's database id is returned in the
   `X-Signature-Id` response header.

## Project layout

```
src/DocumentsApi.Api/
  Controllers/DocumentsController.cs   # POST /api/documents/sign
  Services/PdfSigningService.cs        # stamps the PDF
  Services/DocumentSignatureRepository.cs # persists the audit record
  Data/AppDbContext.cs                 # EF Core DbContext (MySQL via Pomelo)
  Data/Entities/DocumentSignature.cs   # audit record entity
  Data/Migrations/                     # EF Core migrations
  Pdf/EmbeddedFontResolver.cs          # embedded-font PDF font resolver
  Options/PdfSignatureOptions.cs       # stamp placement/size configuration
```

## Running locally

### 1. Start MySQL

```bash
docker compose up -d
```

This starts MySQL 8 on `localhost:3306` with database `documents_api` and
user `documents_api` / `changeme` (see `docker-compose.yml`).

### 2. Configure the connection string

The default connection string in `appsettings.json` already matches the
compose file:

```
Server=localhost;Port=3306;Database=documents_api;User=documents_api;Password=changeme;
```

Override it for other environments via `ConnectionStrings:DocumentsDb`
(environment variable `ConnectionStrings__DocumentsDb`), rather than editing
`appsettings.json`.

### 3. Run the API

```bash
cd src/DocumentsApi.Api
dotnet run
```

EF Core migrations are applied automatically on startup
(`dbContext.Database.Migrate()` in `Program.cs`), so the schema is created
the first time the API runs against an empty database.

Swagger UI is available at `/swagger` in the Development environment.

### 4. Try it

```bash
curl -X POST http://localhost:5203/api/documents/sign \
  -F "file=@/path/to/document.pdf;type=application/pdf" \
  -F "username=jdoe" \
  -o signed.pdf
```

## Managing migrations

```bash
cd src/DocumentsApi.Api
dotnet tool install --global dotnet-ef   # first time only
dotnet ef migrations add <Name> -o Data/Migrations
```

## Configuration

`PdfSignature` section in `appsettings.json` controls stamp placement:

| Key           | Meaning                                             | Default |
|---------------|------------------------------------------------------|---------|
| `PageNumber`  | 1-based page to stamp; `0` or less means last page   | `0`     |
| `FontSize`    | Stamp font size in points                            | `10`    |
| `MarginRight` | Distance from the right edge of the page, in points  | `40`    |
| `MarginBottom`| Distance from the bottom edge of the page, in points | `30`    |
