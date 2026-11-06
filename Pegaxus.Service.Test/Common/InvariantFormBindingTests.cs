using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Pegaxus.API.Extensions;

namespace Pegaxus.Service.Test;

public class InvariantFormBindingTests
{
    private static async Task<ValueProviderResult> ReadAsync(string key, string value)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { [key] = value });
        var factoryContext = new ValueProviderFactoryContext(new ActionContext(context, new RouteData(), new ActionDescriptor()));
        await new InvariantFormValueProviderFactory().CreateValueProviderAsync(factoryContext);
        return Assert.Single(factoryContext.ValueProviders).GetValue(key);
    }

    [Fact]
    public async Task FormFields_AreConvertedWithTheInvariantCulture_WhateverTheServerCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("vi-VN");
        try
        {
            var result = await ReadAsync("temperatureCelsius", "38.2");

            Assert.Equal(CultureInfo.InvariantCulture, result.Culture);
            Assert.Equal(38.2m, Convert.ToDecimal(result.FirstValue, result.Culture));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task AMalformedMultipartBody_BecomesAValueProviderException_LikeTheStockFactory()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data";
        var factoryContext = new ValueProviderFactoryContext(new ActionContext(context, new RouteData(), new ActionDescriptor()));

        await Assert.ThrowsAsync<ValueProviderException>(() => new InvariantFormValueProviderFactory().CreateValueProviderAsync(factoryContext));
    }

    [Fact]
    public async Task RequestsWithoutAForm_AddNoValueProvider()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        var factoryContext = new ValueProviderFactoryContext(new ActionContext(context, new RouteData(), new ActionDescriptor()));

        await new InvariantFormValueProviderFactory().CreateValueProviderAsync(factoryContext);

        Assert.Empty(factoryContext.ValueProviders);
    }
}
