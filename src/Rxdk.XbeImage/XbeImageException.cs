namespace Rxdk.XbeImage;

public sealed class XbeImageException : Exception
{
    public XbeImageException(string message) : base(message)
    {
    }

    public XbeImageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
