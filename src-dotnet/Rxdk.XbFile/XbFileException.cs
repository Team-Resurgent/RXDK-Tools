namespace Rxdk.XbFile;

public sealed class XbFileException : Exception
{
    public XbFileException(string message) : base(message) { }

    public XbFileException(string message, Exception inner) : base(message, inner) { }
}
