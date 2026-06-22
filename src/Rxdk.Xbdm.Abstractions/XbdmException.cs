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
        if (!string.IsNullOrWhiteSpace(detail) && LooksLikeXbdmStatus(detail))
            message = $"{message} {detail.Trim()}";
        return new XbdmException(message, hresult);
    }

    private static bool LooksLikeXbdmStatus(string detail)
    {
        detail = detail.TrimStart();
        return detail.Length >= 3 &&
               char.IsDigit(detail[0]) &&
               char.IsDigit(detail[1]) &&
               char.IsDigit(detail[2]);
    }
}
