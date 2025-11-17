namespace AiFuturesTerminal
{
    public static class DebugFeatures
    {
#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
