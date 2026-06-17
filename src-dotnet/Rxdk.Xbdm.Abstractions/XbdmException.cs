namespace Rxdk.Xbdm;

public sealed class XbdmException : Exception
{
    public int HResultCode { get; }

    public XbdmException(string message, int hresultCode) : base(message)
    {
        HResultCode = hresultCode;
    }

    public static XbdmException FromHResult(string message, int hresult, string? detail = null)
    {
        if (!string.IsNullOrWhiteSpace(detail))
            message = $"{message} {detail}";
        return new XbdmException(message, hresult);
    }
}
