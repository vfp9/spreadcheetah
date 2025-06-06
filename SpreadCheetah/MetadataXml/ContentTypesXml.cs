using SpreadCheetah.Helpers;
using SpreadCheetah.Images.Internal;
using SpreadCheetah.Worksheets;

namespace SpreadCheetah.MetadataXml;

internal struct ContentTypesXml : IXmlWriter<ContentTypesXml>
{
    public static ValueTask WriteAsync(
        ZipArchiveManager zipArchiveManager,
        SpreadsheetBuffer buffer,
        List<WorksheetMetadata> worksheets,
        FileCounter? fileCounter,
        bool hasStylesXml,
        bool includeDocumentProperties,
        CancellationToken token)
    {
        const string entryName = "[Content_Types].xml";
        var writer = new ContentTypesXml(worksheets, fileCounter, hasStylesXml, includeDocumentProperties, buffer);
        return zipArchiveManager.WriteAsync(writer, entryName, buffer, token);
    }

    private static ReadOnlySpan<byte> Header =>
        """<?xml version="1.0" encoding="utf-8"?>"""u8 +
        """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">"""u8 +
        """<Default Extension="xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" />"""u8 +
        """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />"""u8;

    private static ReadOnlySpan<byte> Jpeg => """<Default Extension="jpeg" ContentType="image/jpeg"/>"""u8;
    private static ReadOnlySpan<byte> Png => """<Default Extension="png" ContentType="image/png"/>"""u8;
    private static ReadOnlySpan<byte> Vml => "<Default Extension=\"vml\" ContentType=\"application/vnd.openxmlformats-officedocument.vmlDrawing\"/>"u8;
    private static ReadOnlySpan<byte> Styles => """<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml" />"""u8;
    private static ReadOnlySpan<byte> DrawingStart => """<Override PartName="/xl/drawings/drawing"""u8;
    private static ReadOnlySpan<byte> DrawingEnd => """.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>"""u8;
    private static ReadOnlySpan<byte> TableStart => """<Override PartName="/xl/tables/table"""u8;
    private static ReadOnlySpan<byte> TableEnd => """.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"/>"""u8;
    private static ReadOnlySpan<byte> SheetStart => """<Override PartName="/"""u8;
    private static ReadOnlySpan<byte> SheetEnd => "\" "u8 + """ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml" />"""u8;
    private static ReadOnlySpan<byte> CommentStart => """<Override PartName="/xl/comments"""u8;
    private static ReadOnlySpan<byte> CommentEnd => """.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>"""u8;
    private static ReadOnlySpan<byte> DocProps =>
        """<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>"""u8 +
        """<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>"""u8;
    private static ReadOnlySpan<byte> Footer => "</Types>"u8;

    private readonly List<WorksheetMetadata> _worksheets;
    private readonly FileCounter? _fileCounter;
    private readonly SpreadsheetBuffer _buffer;
    private readonly bool _hasStylesXml;
    private readonly bool _includeDocumentProperties;
    private Element _next;
    private int _nextIndex;

    private ContentTypesXml(
        List<WorksheetMetadata> worksheets,
        FileCounter? fileCounter,
        bool hasStylesXml,
        bool includeDocumentProperties,
        SpreadsheetBuffer buffer)
    {
        _worksheets = worksheets;
        _fileCounter = fileCounter;
        _hasStylesXml = hasStylesXml;
        _includeDocumentProperties = includeDocumentProperties;
        _buffer = buffer;
    }

    public readonly ContentTypesXml GetEnumerator() => this;
    public bool Current { get; private set; }

    public bool MoveNext()
    {
        Current = _next switch
        {
            Element.Header => _buffer.TryWrite(Header),
            Element.ImageTypes => TryWriteImageTypes(),
            Element.Vml => TryWriteVml(),
            Element.Styles => TryWriteStyles(),
            Element.Drawings => TryWriteDrawings(),
            Element.Tables => TryWriteTables(),
            Element.Worksheets => TryWriteWorksheets(),
            Element.Comments => TryWriteComments(),
            Element.DocProps => TryWriteDocProps(),
            _ => _buffer.TryWrite(Footer)
        };

        if (Current)
            ++_next;

        return _next < Element.Done;
    }

    private readonly bool TryWriteImageTypes()
    {
        if (_fileCounter is not { } counter)
            return true;

        var bytes = _buffer.GetSpan();
        var bytesWritten = 0;

        if (counter.EmbeddedImageTypes.HasFlag(EmbeddedImageTypes.Png) && !Png.TryCopyTo(bytes, ref bytesWritten))
            return false;

        if (counter.EmbeddedImageTypes.HasFlag(EmbeddedImageTypes.Jpeg) && !Jpeg.TryCopyTo(bytes, ref bytesWritten))
            return false;

        _buffer.Advance(bytesWritten);
        return true;
    }

    private readonly bool TryWriteStyles()
        => !_hasStylesXml || _buffer.TryWrite(Styles);

    private bool TryWriteDrawings()
    {
        if (_fileCounter is not { } counter)
            return true;

        for (; _nextIndex < counter.WorksheetsWithImages; ++_nextIndex)
        {
            var success = _buffer.TryWrite($"{DrawingStart}{_nextIndex + 1}{DrawingEnd}");
            if (!success)
                return false;
        }

        _nextIndex = 0;
        return true;
    }

    private bool TryWriteTables()
    {
        if (_fileCounter is not { } counter)
            return true;

        for (; _nextIndex < counter.TotalTables; ++_nextIndex)
        {
            if (!_buffer.TryWrite($"{TableStart}{_nextIndex + 1}{TableEnd}"))
                return false;
        }

        _nextIndex = 0;
        return true;
    }

    private readonly bool TryWriteVml()
    {
        var hasNotes = _fileCounter is { WorksheetsWithNotes: > 0 };
        return !hasNotes || _buffer.TryWrite(Vml);
    }

    private bool TryWriteWorksheets()
    {
        var worksheets = _worksheets;

        for (; _nextIndex < worksheets.Count; ++_nextIndex)
        {
            var worksheet = worksheets[_nextIndex];
            var success = _buffer.TryWrite($"{SheetStart}{worksheet.Path}{SheetEnd}");
            if (!success)
                return false;
        }

        _nextIndex = 0;
        return true;
    }

    private bool TryWriteComments()
    {
        if (_fileCounter is not { } counter)
            return true;

        for (; _nextIndex < counter.WorksheetsWithNotes; ++_nextIndex)
        {
            var success = _buffer.TryWrite($"{CommentStart}{_nextIndex + 1}{CommentEnd}");
            if (!success)
                return false;
        }

        _nextIndex = 0;
        return true;
    }

    private readonly bool TryWriteDocProps()
        => !_includeDocumentProperties || _buffer.TryWrite(DocProps);

    private enum Element
    {
        Header,
        ImageTypes,
        Vml,
        Styles,
        Drawings,
        Tables,
        Worksheets,
        Comments,
        DocProps,
        Footer,
        Done
    }
}
