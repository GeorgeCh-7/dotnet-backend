using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DotnetBackend.Models;

/// <summary>Implemented by request DTOs so <see cref="DotnetBackend.Middleware.ValidationFilter{T}"/> can run validation before the handler.</summary>
public interface IValidatable
{
    IEnumerable<string> Validate();
}

public class CreateUserRequest : IValidatable
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return "name is required";

        if (string.IsNullOrWhiteSpace(Email))
            yield return "email is required";
        else if (!Regex.IsMatch(Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase))
            yield return "Invalid email format";

        if (string.IsNullOrWhiteSpace(Role))
            yield return "role is required";
    }
}

public class CreateTaskRequest : IValidatable
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            yield return "title is required";

        if (string.IsNullOrWhiteSpace(Status))
            yield return "status is required";
        else if (!ValidStatuses.Contains(Status))
            yield return "status must be one of: pending, in-progress, completed";

        if (UserId is null)
            yield return "userId is required";
    }
}

public class UpdateTaskRequest : IValidatable
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    public IEnumerable<string> Validate()
    {
        // All fields are optional on update — only validate what is provided
        if (Status is not null && !ValidStatuses.Contains(Status))
            yield return "status must be one of: pending, in-progress, completed";
    }
}

internal static class ValidStatuses
{
    private static readonly string[] Values = ["pending", "in-progress", "completed"];
    public static bool Contains(string value) => Values.Contains(value);
}
