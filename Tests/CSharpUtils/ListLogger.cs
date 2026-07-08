using LLiquidLink.Logger;
using System.Collections.Generic;

public class ListLogger : ILogger
{
    public LogLevel MinLevel { get; set; }

    public List<string> Entries = new List<string>();

    public ListLogger()
    {
        MinLevel = LogLevel.Debug;
    }

    public void Info(string msg)
    {
        Entries.Add("INFO: " + msg);
        UnityEngine.Debug.LogError("[UniLiquidLink] " + msg);
    }

    public void Debug(string msg)
    {
        Entries.Add("DEBUG: " + msg);
        UnityEngine.Debug.LogError("[UniLiquidLink] " + msg);
    }

    public void InfoFormat(string format, params object[] args)
    {
        Entries.Add("INFO: " + string.Format(format, args));
        UnityEngine.Debug.LogErrorFormat("[UniLiquidLink] " + format, args);
    }

    public void DebugFormat(string format, params object[] args)
    {
        Entries.Add("DEBUG: " + string.Format(format, args));
        UnityEngine.Debug.LogErrorFormat("[UniLiquidLink] " + format, args);
    }
}
