namespace iRacingLauncher.ViewModels;

/// <summary>Selection rules shared by the Settings lists.</summary>
public static class ListSelection
{
    /// <summary>
    /// Which index to select after removing the item at <paramref name="removedIndex"/>:
    /// the item that slid into its place, or the new last item, or -1 if the list is
    /// now empty. Keeps the details pane from showing something that was deleted.
    /// </summary>
    public static int AfterRemoval(int removedIndex, int countAfterRemoval) =>
        countAfterRemoval <= 0 ? -1 : System.Math.Min(removedIndex, countAfterRemoval - 1);
}
