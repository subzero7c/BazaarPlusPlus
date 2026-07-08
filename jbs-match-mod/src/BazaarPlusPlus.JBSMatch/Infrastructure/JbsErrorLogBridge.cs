#nullable enable
using System;

namespace BazaarPlusPlus.JBSMatch;

public static class JbsErrorLogBridge
{
    public static void WriteExternalError(string category, string message, string? details = null)
    {
        try
        {
            JbsLog.ExternalError(category, message, details);
        }
        catch
        {
            // External logging must never break the caller.
        }
    }
}
