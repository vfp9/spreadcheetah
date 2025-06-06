namespace SpreadCheetah.TestHelpers.Assertions;

public interface ISpreadsheetAssertDocumentProperties
{
    string? Application { get; }
    string? Author { get; }
    string? Subject { get; }
    string? Title { get; }
    DateTime? Created { get; }
}
