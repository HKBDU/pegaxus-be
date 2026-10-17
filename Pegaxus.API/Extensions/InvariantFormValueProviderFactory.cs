using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Pegaxus.API.Extensions;

/// <summary>
/// Reads multipart and form fields with the invariant culture. The API contracts write decimals with a dot, but the stock
/// factory uses the server's culture, so on a host with a comma decimal separator (for example vi-VN) "38.2" became 382.
/// Query string and route values are already invariant.
/// </summary>
public sealed class InvariantFormValueProviderFactory : IValueProviderFactory
{
    public async Task CreateValueProviderAsync(ValueProviderFactoryContext context)
    {
        var request = context.ActionContext.HttpContext.Request;
        if (!request.HasFormContentType) return;
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // Same as the stock factory: a malformed body becomes a 400 instead of an unhandled exception.
            throw new ValueProviderException($"Failed to read the request form. {exception.Message}", exception);
        }
        context.ValueProviders.Add(new FormValueProvider(BindingSource.Form, form, CultureInfo.InvariantCulture));
    }
}
