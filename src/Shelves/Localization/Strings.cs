using System.Globalization;

namespace Shelves.Localization;

/// <summary>English and Arabic UI text.</summary>
public static class L
{
    public static bool Ar { get; set; }

    static readonly Dictionary<string, (string en, string ar)> D = new()
    {
        ["AppName"] = ("Shelves", "الرفوف"),
        ["NewShelf"] = ("New shelf", "رف جديد"),
        ["CreateShelfHere"] = ("Create shelf here", "إنشاء رف هنا"),
        ["Cancel"] = ("Cancel", "إلغاء"),
        ["Save"] = ("Save", "حفظ"),
        ["DefaultTitle"] = ("Shelf", "رف"),
        ["DefaultPortalTitle"] = ("{0}", "{0}"),
        ["Rename"] = ("Rename", "إعادة تسمية"),
        ["RollUp"] = ("Roll up", "طي"),
        ["Unroll"] = ("Open", "فتح"),
        ["Dock"] = ("Dock", "تثبيت على الحافة"),
        ["DockLeft"] = ("Left edge", "الحافة اليسرى"),
        ["DockRight"] = ("Right edge", "الحافة اليمنى"),
        ["DockTop"] = ("Top edge", "الحافة العليا"),
        ["DockBottom"] = ("Bottom edge", "الحافة السفلى"),
        ["Undock"] = ("Undock", "إلغاء التثبيت"),
        ["Sort"] = ("Sort by", "الترتيب حسب"),
        ["SortNone"] = ("My order", "ترتيبي"),
        ["SortName"] = ("Name", "الاسم"),
        ["SortType"] = ("Type", "النوع"),
        ["SortDate"] = ("Date modified", "تاريخ التعديل"),
        ["IconSize"] = ("Icon size", "حجم الأيقونات"),
        ["SizeSmall"] = ("Small", "صغير"),
        ["SizeMedium"] = ("Medium", "متوسط"),
        ["SizeLarge"] = ("Large", "كبير"),
        ["MakePortal"] = ("Show a folder…", "عرض مجلد…"),
        ["StopPortal"] = ("Stop showing the folder", "إيقاف عرض المجلد"),
        ["OpenFolder"] = ("Open in Explorer", "فتح في المستكشف"),
        ["DeleteShelf"] = ("Delete shelf", "حذف الرف"),
        ["DeleteShelfAsk"] = ("Delete the shelf \"{0}\"? Its icons go back to the desktop. No files are deleted.",
                              "حذف الرف \"{0}\"؟ ستعود أيقوناته إلى سطح المكتب، ولن يُحذف أي ملف."),
        ["Empty"] = ("Drag icons here", "اسحب الأيقونات إلى هنا"),
        ["EmptyPortal"] = ("This folder is empty", "هذا المجلد فارغ"),
        ["PortalMissing"] = ("Folder not found", "المجلد غير موجود"),
        ["HideAll"] = ("Hide desktop", "إخفاء سطح المكتب"),
        ["ShowAll"] = ("Show desktop", "إظهار سطح المكتب"),
        ["Snapshots"] = ("Snapshots", "اللقطات"),
        ["SaveSnapshot"] = ("Save snapshot", "حفظ لقطة"),
        ["SnapshotName"] = ("Snapshot {0}", "لقطة {0}"),
        ["AutoSnapshot"] = ("Automatic", "تلقائية"),
        ["NoSnapshots"] = ("No snapshots yet", "لا توجد لقطات بعد"),
        ["RestoreSnapshotAsk"] = ("Restore the layout from \"{0}\"? Your current shelves are replaced.",
                                  "استعادة الترتيب من \"{0}\"؟ ستُستبدل الرفوف الحالية."),
        ["ImportFences"] = ("Import from Fences", "استيراد من Fences"),
        ["ImportedFences"] = ("Added {0} shelves from Fences.", "أُضيفت {0} رفوف من Fences."),
        ["RestoreDesktop"] = ("Restore desktop", "استعادة سطح المكتب"),
        ["RestoreDesktopAsk"] = ("Put every icon back on the desktop and remove all shelves?",
                                 "إعادة كل الأيقونات إلى سطح المكتب وإزالة كل الرفوف؟"),
        ["Settings"] = ("Settings", "الإعدادات"),
        ["Exit"] = ("Exit", "خروج"),
        ["Version"] = ("Version {0}", "الإصدار {0}"),
        ["CheckForUpdates"] = ("Check for updates", "التحقق من التحديثات"),
        ["FencesRunning"] = ("Stardock Fences is running. Shelves and Fences both move desktop icons, so close Fences before using Shelves.",
                             "برنامج Stardock Fences يعمل. كلاهما ينقل أيقونات سطح المكتب، لذا أغلق Fences قبل استخدام الرفوف."),
        ["ExplorerMissing"] = ("Waiting for the desktop…", "في انتظار سطح المكتب…"),
        ["MissingItems"] = ("{0} shelved icons are no longer on the desktop.", "{0} من أيقونات الرفوف لم تعد على سطح المكتب."),
        ["AutoArrangeOff"] = ("Auto arrange was turned off on the desktop so shelves can hide icons. It comes back when Shelves exits.",
                              "أُوقف الترتيب التلقائي لسطح المكتب كي تتمكن الرفوف من إخفاء الأيقونات، ويعود عند الخروج."),
        // Settings
        ["General"] = ("General", "عام"),
        ["Appearance"] = ("Appearance", "المظهر"),
        ["RollUpPage"] = ("Roll up", "الطي"),
        ["Updates"] = ("Updates", "التحديثات"),
        ["About"] = ("About", "حول"),
        ["StartWithWindows"] = ("Start with Windows", "التشغيل مع ويندوز"),
        ["Language"] = ("Language", "اللغة"),
        ["LangAuto"] = ("Windows", "لغة ويندوز"),
        ["DoubleClickHides"] = ("Double-click the desktop to hide everything", "النقر المزدوج على سطح المكتب يخفي كل شيء"),
        ["Opacity"] = ("Background strength (%)", "قوة الخلفية (%)"),
        ["CornerRadius"] = ("Corner radius (px)", "استدارة الزوايا (بكسل)"),
        ["TitleAlign"] = ("Title", "العنوان"),
        ["AlignLeft"] = ("Start", "البداية"),
        ["AlignCenter"] = ("Center", "الوسط"),
        ["AlignRight"] = ("End", "النهاية"),
        ["LabelLines"] = ("Label lines", "أسطر التسمية"),
        ["DefaultIconSize"] = ("Icon size for new shelves", "حجم الأيقونات للرفوف الجديدة"),
        ["HoverToPeek"] = ("Hover over a rolled-up shelf to open it", "فتح الرف المطوي عند مرور المؤشر"),
        ["HoverDelay"] = ("Open after (ms)", "الفتح بعد (مللي ثانية)"),
        ["LeaveDelay"] = ("Roll up after leaving (ms)", "الطي بعد المغادرة (مللي ثانية)"),
        ["AutoCheck"] = ("Check for updates automatically", "التحقق من التحديثات تلقائيًا"),
        ["CheckNow"] = ("Check now", "تحقق الآن"),
        ["CheckingUpdates"] = ("Checking for updates…", "جارٍ البحث عن تحديثات…"),
        ["UpdateDownloading"] = ("Downloading {0}: {1}", "جارٍ تنزيل {0}: {1}"),
        ["UpdateVerifying"] = ("Checking the download…", "جارٍ التحقق من التنزيل…"),
        ["UpdateRestarting"] = ("Restarting into {0}…", "جارٍ إعادة التشغيل إلى {0}…"),
        ["UpToDate"] = ("You're on the latest version ({0}).", "لديك أحدث إصدار ({0})."),
        ["UpdateAvailable"] = ("Version {0} is available. Click to install.", "الإصدار {0} متاح. انقر للتثبيت."),
        ["UpdateFailed"] = ("Couldn't install the update. Opening the download page.", "تعذر تثبيت التحديث. سيتم فتح صفحة التنزيل."),
        ["UpdateCheckFailed"] = ("Couldn't check for updates.", "تعذر التحقق من التحديثات."),
        ["Updated"] = ("Updated to version {0}.", "تم التحديث إلى الإصدار {0}."),
        ["GitHubRepo"] = ("GitHub page", "صفحة GitHub"),
        ["ReleaseNotes"] = ("Release notes", "ملاحظات الإصدار"),
        ["AboutText"] = ("Group your desktop icons into shelves that roll up, on any edge of the screen.",
                         "رتّب أيقونات سطح المكتب في رفوف تنطوي، على أي حافة من الشاشة."),
        ["SnapshotsPage"] = ("Snapshots", "اللقطات"),
        ["SnapshotsHint"] = ("Shelves saves the layout every day and before updates, and keeps the last 10.",
                             "تحفظ الرفوف الترتيب كل يوم وقبل التحديثات، وتحتفظ بآخر 10 لقطات."),
        ["Restore"] = ("Restore", "استعادة"),
        ["Delete"] = ("Delete", "حذف"),
    };

    public static string T(string key) => D.TryGetValue(key, out var v) ? (Ar ? v.ar : v.en) : key;
    public static string F(string key, params object[] args) => string.Format(T(key), args);

    /// <summary>"512 B", "1.5 KB", "32.8 MB" in binary units.</summary>
    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double v = Math.Max(0, bytes);
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v.ToString(v < 10 ? "0.##" : v < 100 ? "0.#" : "0", CultureInfo.InvariantCulture)} {units[u]}";
    }

    public static CultureInfo Culture => Ar ? CultureInfo.GetCultureInfo("ar-EG") : CultureInfo.GetCultureInfo("en-US");

    /// <summary>English or Arabic from the setting, or from Windows when it is empty.</summary>
    public static void Apply(string setting) =>
        Ar = setting == "ar" || (setting == "" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar");
}
