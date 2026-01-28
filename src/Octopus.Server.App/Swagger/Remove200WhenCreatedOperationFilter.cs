using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Octopus.Server.App.Swagger;

/// <summary>
/// Swagger operation filter that removes the default 200 OK response when:
/// 1. A 201 Created response is defined with a schema (for POST create endpoints)
/// 2. A 204 No Content response is defined (for DELETE/void endpoints)
/// 3. A 302 Found response is defined (for redirect endpoints)
///
/// This fixes NSwag code generation which incorrectly treats 200 as the
/// primary success response (returning void) and other status codes as exceptions.
/// </summary>
public class Remove200WhenCreatedOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Check if there's a 200 response
        if (!operation.Responses.TryGetValue("200", out var response200))
        {
            return;
        }

        // If 200 has a proper schema, keep it
        if (ResponseHasSchema(response200))
        {
            return;
        }

        // Remove 200 if 201 Created is defined with a schema
        if (operation.Responses.TryGetValue("201", out var response201) && ResponseHasSchema(response201))
        {
            operation.Responses.Remove("200");
            return;
        }

        // Remove 200 if 204 No Content is defined (delete/void operations)
        if (operation.Responses.ContainsKey("204"))
        {
            operation.Responses.Remove("200");
            return;
        }

        // Remove 200 if 302 Found is defined (redirect operations)
        if (operation.Responses.ContainsKey("302"))
        {
            operation.Responses.Remove("200");
            return;
        }
    }

    private static bool ResponseHasSchema(OpenApiResponse? response)
    {
        if (response?.Content == null || response.Content.Count == 0)
        {
            return false;
        }

        // Check if any content type has a non-empty schema
        return response.Content.Any(c =>
            c.Value?.Schema != null &&
            (c.Value.Schema.Reference != null ||
             c.Value.Schema.Type != null ||
             c.Value.Schema.AllOf?.Count > 0 ||
             c.Value.Schema.OneOf?.Count > 0 ||
             c.Value.Schema.AnyOf?.Count > 0 ||
             c.Value.Schema.Properties?.Count > 0));
    }
}
