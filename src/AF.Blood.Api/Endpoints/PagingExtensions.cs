using AF.Blood.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AF.Blood.Api.Endpoints;

public static class PagingExtensions
{
    public static (int Page, int PageSize) GetPaging(this HttpContext context)
    {
        var page = ParsePositive(context, "page", 1);
        var pageSize = Math.Clamp(ParsePositive(context, "pageSize", 20), 1, 100);
        return (page, pageSize);
    }

    private static int ParsePositive(HttpContext context, string key, int fallback) =>
        int.TryParse(context.Request.Query[key], out var value) && value > 0 ? value : fallback;
}
