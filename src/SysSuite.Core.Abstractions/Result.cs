namespace SysSuite.Core.Abstractions;

public enum ErrorType
{
    None,
    NotFound,
    InvalidInput,
    Conflict,
    AccessDenied,
    DependencyMissing,
    Cancelled,
    Internal
}

public readonly record struct Result(ErrorType Error, string Message, string? DiagnosticId = null)
{
    public bool IsSuccess => Error == ErrorType.None;

    public static Result Success() => new(ErrorType.None, string.Empty);

    public static Result Failure(ErrorType error, string message) => new(error, message);
}

public readonly record struct Result<T>(ErrorType Error, string Message, T? Value = default, string? DiagnosticId = null)
{
    public bool IsSuccess => Error == ErrorType.None;

}

public static class ResultExtensions
{
    public static Result<T> Success<T>(this T value) => new(ErrorType.None, string.Empty, value);

    public static Result<T> Failure<T>(ErrorType error, string message) => new(error, message);
}
