public static class MultipleErrors
{
    public static void Run()
    {
        MissingType first = new();
        AnotherMissingType second = new();
        _ = first;
        _ = second;
    }
}
