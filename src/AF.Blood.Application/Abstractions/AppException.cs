namespace AF.Blood.Application.Abstractions;

/// <summary>Typed application error carried to the ProblemDetails handler.</summary>
public class AppException(string errorCode, string title, string detail, int statusCode) : Exception(detail)
{
    public string ErrorCode { get; } = errorCode;
    public string Title { get; } = title;
    public int StatusCode { get; } = statusCode;

    public static AppException Unauthorized(string detail) =>
        new("unauthorized", "Unauthorized", detail, 401);

    public static AppException NotFound(string detail) =>
        new("not_found", "Resource not found", detail, 404);

    public static AppException Forbidden(string detail) =>
        new("forbidden", "Not permitted", detail, 403);

    public static AppException Conflict(string detail) =>
        new("conflict", "Conflict", detail, 409);

    public static AppException Invalid(string detail) =>
        new("validation_failed", "Validation failed", detail, 400);
}
