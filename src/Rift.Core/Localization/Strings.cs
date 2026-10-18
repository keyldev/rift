using System.Globalization;
using System.Resources;

namespace Rift.Core.Localization;

/// <summary>
/// UI strings. English is the neutral language (Strings.resx), translations live in Strings.{culture}.resx.
/// To add a string: add it to every .resx and a property here; StringsTests checks they stay in sync.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Resources = new("Rift.Core.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>Overrides the UI culture; null follows the Windows display language.</summary>
    public static CultureInfo? Culture { get; set; }

    public static IReadOnlyList<string> Supported { get; } = ["en", "ru"];

    public static string Get(string key) => Resources.GetString(key, Culture) ?? key;

    private static string Format(string key, params object[] args) =>
        string.Format(Culture ?? CultureInfo.CurrentUICulture, Get(key), args);

    public static string SizeB => Get(nameof(SizeB));
    public static string SizeKB => Get(nameof(SizeKB));
    public static string SizeMB => Get(nameof(SizeMB));
    public static string SizeGB => Get(nameof(SizeGB));
    public static string SizeTB => Get(nameof(SizeTB));
    public static string KindFolder => Get(nameof(KindFolder));
    public static string KindImage => Get(nameof(KindImage));
    public static string KindVideo => Get(nameof(KindVideo));
    public static string KindAudio => Get(nameof(KindAudio));
    public static string KindArchive => Get(nameof(KindArchive));
    public static string KindCode => Get(nameof(KindCode));
    public static string KindDocument => Get(nameof(KindDocument));
    public static string KindPdf => Get(nameof(KindPdf));
    public static string KindExecutable => Get(nameof(KindExecutable));
    public static string KindFile => Get(nameof(KindFile));
    public static string ItemsCount(object a0) => Format(nameof(ItemsCount), a0);
    public static string SelectedCount(object a0) => Format(nameof(SelectedCount), a0);
    public static string ErrorAccessDenied => Get(nameof(ErrorAccessDenied));
    public static string ErrorFolderNotFound => Get(nameof(ErrorFolderNotFound));
    public static string ErrorIntoItself => Get(nameof(ErrorIntoItself));
    public static string SectionQuickAccess => Get(nameof(SectionQuickAccess));
    public static string SectionDrives => Get(nameof(SectionDrives));
    public static string SectionExplorer => Get(nameof(SectionExplorer));
    public static string PlaceDesktop => Get(nameof(PlaceDesktop));
    public static string PlaceDownloads => Get(nameof(PlaceDownloads));
    public static string PlaceDocuments => Get(nameof(PlaceDocuments));
    public static string PlaceHome => Get(nameof(PlaceHome));
    public static string DriveLocal => Get(nameof(DriveLocal));
    public static string DriveFree(object a0, object a1) => Format(nameof(DriveFree), a0, a1);
    public static string ExplorerMenuText => Get(nameof(ExplorerMenuText));
    public static string ExplorerMenuToggle => Get(nameof(ExplorerMenuToggle));
    public static string ExplorerMenuToggleTip => Get(nameof(ExplorerMenuToggleTip));
    public static string TipSinglePane => Get(nameof(TipSinglePane));
    public static string TipSplitPane => Get(nameof(TipSplitPane));
    public static string TipTheme => Get(nameof(TipTheme));
    public static string TipMinimize => Get(nameof(TipMinimize));
    public static string TipMaximize => Get(nameof(TipMaximize));
    public static string TipClose => Get(nameof(TipClose));
    public static string TipNewTab => Get(nameof(TipNewTab));
    public static string TipBack => Get(nameof(TipBack));
    public static string TipForward => Get(nameof(TipForward));
    public static string TipUp => Get(nameof(TipUp));
    public static string TipRefresh => Get(nameof(TipRefresh));
    public static string TipPause => Get(nameof(TipPause));
    public static string TipResume => Get(nameof(TipResume));
    public static string TipCancel => Get(nameof(TipCancel));
    public static string TipDismiss => Get(nameof(TipDismiss));
    public static string ColumnName => Get(nameof(ColumnName));
    public static string ColumnModified => Get(nameof(ColumnModified));
    public static string ColumnType => Get(nameof(ColumnType));
    public static string ColumnSize => Get(nameof(ColumnSize));
    public static string HintOtherPane => Get(nameof(HintOtherPane));
    public static string HintCopy => Get(nameof(HintCopy));
    public static string HintMove => Get(nameof(HintMove));
    public static string HintRename => Get(nameof(HintRename));
    public static string HintDelete => Get(nameof(HintDelete));
    public static string HintNewFolder => Get(nameof(HintNewFolder));
    public static string HintUndo => Get(nameof(HintUndo));
    public static string OpCopy => Get(nameof(OpCopy));
    public static string OpMove => Get(nameof(OpMove));
    public static string OpDelete => Get(nameof(OpDelete));
    public static string OpUndo => Get(nameof(OpUndo));
    public static string OpDone => Get(nameof(OpDone));
    public static string OpCanceled => Get(nameof(OpCanceled));
    public static string OpPaused => Get(nameof(OpPaused));
    public static string OpFailed => Get(nameof(OpFailed));
    public static string OpTitle(object a0, object a1, object a2) => Format(nameof(OpTitle), a0, a1, a2);
    public static string OpBytes(object a0, object a1) => Format(nameof(OpBytes), a0, a1);
    public static string OpErrors(object a0) => Format(nameof(OpErrors), a0);
    public static string OpUndoTitle(object a0) => Format(nameof(OpUndoTitle), a0);
    public static string ConflictTitle => Get(nameof(ConflictTitle));
    public static string ConflictMessage(object a0, object a1) => Format(nameof(ConflictMessage), a0, a1);
    public static string ConflictIncoming(object a0, object a1) => Format(nameof(ConflictIncoming), a0, a1);
    public static string ConflictExisting(object a0, object a1) => Format(nameof(ConflictExisting), a0, a1);
    public static string ConflictReplace => Get(nameof(ConflictReplace));
    public static string ConflictSkip => Get(nameof(ConflictSkip));
    public static string ConflictKeepBoth => Get(nameof(ConflictKeepBoth));
    public static string ConflictApplyToAll => Get(nameof(ConflictApplyToAll));
    public static string CommonCancel => Get(nameof(CommonCancel));
    public static string DeleteTitle => Get(nameof(DeleteTitle));
    public static string DeletePermanentTitle => Get(nameof(DeletePermanentTitle));
    public static string DeleteOne(object a0) => Format(nameof(DeleteOne), a0);
    public static string DeleteMany(object a0) => Format(nameof(DeleteMany), a0);
    public static string DeletePermanentWarning => Get(nameof(DeletePermanentWarning));
    public static string DeleteToBin => Get(nameof(DeleteToBin));
    public static string DeletePermanent => Get(nameof(DeletePermanent));
    public static string RenameInvalidChars => Get(nameof(RenameInvalidChars));
    public static string RenameEmpty => Get(nameof(RenameEmpty));
    public static string RenameExists(object a0) => Format(nameof(RenameExists), a0);
    public static string NewFolderName => Get(nameof(NewFolderName));
    public static string CommonOk => Get(nameof(CommonOk));
    public static string ErrorTitle => Get(nameof(ErrorTitle));
    public static string RenameFailedTitle => Get(nameof(RenameFailedTitle));
    public static string NewFolderFailedTitle => Get(nameof(NewFolderFailedTitle));
    public static string OpRecycle => Get(nameof(OpRecycle));
    public static string OpUndoFailed => Get(nameof(OpUndoFailed));
    public static string OpErrorsMore(object a0) => Format(nameof(OpErrorsMore), a0);
    public static string HintPaste => Get(nameof(HintPaste));
    public static string DropMove(object a0) => Format(nameof(DropMove), a0);
    public static string DropCopy(object a0) => Format(nameof(DropCopy), a0);
    public static string DropHintMove => Get(nameof(DropHintMove));
    public static string DropHintCopy => Get(nameof(DropHintCopy));
}
