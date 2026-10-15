# Crater It! investigation

Investigation date: 2026-10-15. Paths are relative to the repository root.

This report is based on static source inspection. No application code was modified, packages installed, builds run, credentials accessed, database commands executed, or live generation requests made. Creating this report is the sole file change authorized by the report request. Deployment configuration and live behavior remain unverified.

## 1. Detail components

| Component | Implementation | Behavior |
| --- | --- | --- |
| Product Detail | `FlyerMonkey/FlyerMonkey.Shared/Pages/ProductDetail.razor` | Route `/products/{Id:int}`. Injects `IProductService`, retrieves the product list and selects by ID. Contains a disabled “Crater It! — Coming Soon” button. |
| Offer Detail | `FlyerMonkey/FlyerMonkey.Shared/Pages/OfferDetail.razor` | Route `/offers/{Id:int}`. Injects `IOfferService`, retrieves the offer list and selects by offer ID. Contains a disabled “Crater It! — Coming Soon” button. |

Each component has an adjacent `.razor.css` file. Both are in the shared Razor project used by the web and MAUI applications. `FlyerMonkey/FlyerMonkey.Shared/Routes.razor` provides shared routing. `FlyerMonkey/FlyerMonkey.Web/Components/App.razor` renders routes using Interactive Server mode.

## 2. Current image lifecycle

The existing reviewer generation flow is:

`Product metadata → generation API → local JPEG → Azure Blob Storage → Products.ImageBlobPath → product image API → detail-page <img>`

### Generate and store locally

- `FlyerMonkey.Reviewer.Windows/MainWindow.xaml.cs`, `CraterItButton_Click`, already implements a generation action. It builds a prompt, calls the generation API, saves bytes to a GUID-named `.jpg` under the hard-coded local `DATA/ProductImages` directory and assigns the path to the extracted product's `ImageFilePath`.
- `FlyerMonkey.Reviewer.Windows/Services/ProductImageService.cs` builds the prompt from brand, product name, variant and pack size.
- `FlyerMonkey.Reviewer.Windows/Services/ImageGenerationApiService.cs` posts the prompt to `api/images/generate` and reads the returned bytes.

The handler exists in source; its successful execution was not tested during this investigation.

### Upload and persist the reference

- `FlyerMonkey.Reviewer.Windows/Services/ProductImageUploadService.cs` uploads a local file to the Azure Blob container `product-images`, using its filename as the blob name. Upload allows overwriting an existing filename and returns `product-images/{filename}`.
- The reviewer import flow in `FlyerMonkey.Reviewer.Windows/MainWindow.xaml.cs` uploads the image, assigns that reference to `Product.ImageBlobPath`, then inserts the product through the repository.
- `SQLServerConnection/Data/ProductRepository.cs` inserts and reads `Products.ImageBlobPath`. The product record stores a blob reference, not image bytes.
- `FlyerMonkey/FlyerMonkey.Shared/Model/Product.cs` exposes `ImageBlobPath`. `FlyerMonkey/FlyerMonkey.Shared/Model/OfferSummary.cs` also exposes an image path and the associated `ProductID`.

### Retrieve and display

- `FlyerMonkey.Api/Controllers/ProductsController.cs` exposes `GET /api/products/{id}/image`. It loads the product, resolves its image path and returns a stream with content type `image/jpeg`. Missing product, missing path or missing blob produces 404.
- `FlyerMonkey.Api/Services/ProductImageService.cs` splits `ImageBlobPath` into container and blob names, checks existence and downloads a streaming response from Azure Blob Storage.
- Both detail pages use `<img>` URLs pointing to that endpoint on the hard-coded deployed API host.
- Product Detail requests the image only when `ImageBlobPath` is populated. Offer Detail requests it when `ProductID > 0`.
- Both use `/_content/FlyerMonkey.Shared/images/product-placeholder.png` as a fallback. The asset exists at `FlyerMonkey/FlyerMonkey.Shared/wwwroot/images/product-placeholder.png`.
- Their adjacent CSS uses `object-fit: contain` and responsive height limits.

## 3. Reusable services and endpoints

| Service or endpoint | Source | Use for the proof of concept |
| --- | --- | --- |
| `POST /api/images/generate` | `FlyerMonkey.Api/Controllers/ImagesController.cs` | Accepts `{ "prompt": "..." }` and returns JPEG bytes without saving them. |
| `ImageGenerationService` | `FlyerMonkey.Api/Services/ImageGenerationService.cs` | Existing server-side OpenAI generation integration. Currently configures `gpt-image-1`, 1024×1024 JPEG output and compression 80. |
| `GenerateImageRequest` | `FlyerMonkey.Api/Models/GenerateImageRequest.cs` | Existing prompt DTO. |
| Reviewer generation client | `FlyerMonkey.Reviewer.Windows/Services/ImageGenerationApiService.cs` | Example of POST JSON, success checking and byte-array retrieval. It is currently specific to the Windows reviewer. |
| Reviewer prompt builder | `FlyerMonkey.Reviewer.Windows/Services/ProductImageService.cs` | Existing product prompt pattern. |
| Shared product client | `FlyerMonkey/FlyerMonkey.Shared/Services/ProductApiService.cs` | Typed-client pattern; currently retrieves the product list. |
| Shared offer client | `FlyerMonkey/FlyerMonkey.Shared/Services/OfferApiService.cs` | Typed-client pattern and offer metadata retrieval. Its GET retry behavior should not be copied automatically to generation POST requests. |
| `GET /api/products` and `GET /api/products/{id}` | `FlyerMonkey.Api/Controllers/ProductsController.cs` | Product metadata for prompt construction. The current detail page already has the necessary fields. |
| `GET /api/products/{id}/image` | `FlyerMonkey.Api/Controllers/ProductsController.cs` | Existing canonical image display; not an image-editing endpoint. |
| `GET /api/offers`, `/api/offers/current`, `/api/offers/previous` | `FlyerMonkey.Api/Controllers/OffersController.cs` | Offer and product metadata. No individual-offer GET was found in this controller. |

`FlyerMonkey.Api/Program.cs` already registers the generation service. `FlyerMonkey.Api/FlyerMonkey.Api.csproj` already references OpenAI and Azure Blob packages, so the proposed reuse does not require an additional generation package.

The existing generation operation accepts text only. It does not pass the stored product image to the model. Editing that image would require additional implementation.

The inspected generation endpoint has no explicit prompt validation, authorization attribute or rate limit. `UseAuthorization()` in the API startup does not by itself restrict this endpoint. Configuration availability and deployed protections were not verified. API credentials should continue to remain server-side.

## 4. Smallest safe working proof of concept

Assumption: “Crater It!” creates a product image from descriptive fields, matching the current reviewer behavior. The recommended scope is a temporary preview on both detail pages, with no persistence.

1. Add `FlyerMonkey/FlyerMonkey.Shared/Services/ImageGenerationApiService.cs` using an injected typed `HttpClient`. Reuse `POST /api/images/generate`, with an explicit timeout, cancellation and useful error handling. Do not automatically retry generation requests because a retry may generate another image and incur another charge.
2. Register that client in `FlyerMonkey/FlyerMonkey.Web/Program.cs` and `FlyerMonkey/FlyerMonkey/MauiProgram.cs`, following the existing product/offer client registrations. Use the intended development API address for controlled PoC testing rather than accidentally targeting the deployed API.
3. Wire the existing buttons in `ProductDetail.razor` and `OfferDetail.razor`. Construct a bounded prompt from name, brand, variant and pack size. Show progress, disable duplicate submissions and provide a recoverable error message.
4. Show the returned JPEG as an in-memory `data:image/jpeg;base64,...` preview labelled as generated. Preserve the saved product image. Clear preview state on route changes and cancel or ignore stale responses so a result cannot appear for a different product.
5. Add server-side blank/length validation in `FlyerMonkey.Api/Models/GenerateImageRequest.cs` or the controller, and propagate cancellation through `ImagesController` and `ImageGenerationService`.
6. Keep the PoC in a controlled development environment. Before enabling generation publicly, implement an explicit access policy and request limits to control the existing paid generation endpoint.
7. After implementation approval and separate authorization for execution, verify successful preview generation, API failure/timeout recovery, repeated-click prevention and navigation during generation. Use controlled client responses to verify UI behavior before making a paid generation request.

### Expected application changes after approval

- One new shared generation client service.
- Client registrations in the web and MAUI startup files.
- Button handlers and preview state in the two detail components.
- Small API validation and cancellation changes.

No blob upload, database write, schema change, canonical image replacement, new persistence endpoint or new package is needed for this preview scope. Existing startup and data loading still depend on the application's configured infrastructure; preview-only generation does not remove those dependencies.

Implementation is pending approval. The original restrictions on builds, package installation, credential access and database commands remain in force unless explicitly changed.
