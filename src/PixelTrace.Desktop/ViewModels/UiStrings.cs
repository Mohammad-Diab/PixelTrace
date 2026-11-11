namespace PixelTrace.Desktop.ViewModels;

public sealed class UiStrings(bool arabic)
{
    private static readonly Dictionary<string, (string En, string Ar)> Words = new()
    {
        ["Description"] = ("Image forensics", "فحص الصور"),
        ["Open"] = ("Open image", "فتح صورة"), ["Reanalyze"] = ("Reanalyze", "إعادة التحليل"),
        ["Save"] = ("Save result", "حفظ النتيجة"), ["Analyze"] = ("Analyze", "تحليل"),
        ["Cancel"] = ("Cancel", "إلغاء"), ["Setup"] = ("Analyze image", "تحليل الصورة"),
        ["Method"] = ("Method", "طريقة الفحص"), ["BlockSize"] = ("Block size (px)", "حجم البلوك (بكسل)"),
        ["ElaQuality"] = ("ELA quality", "جودة ELA"), ["CurrentImage"] = ("Current image (locked)", "الصورة الحالية · ثابتة"),
        ["ChooseImage"] = ("Choose an image", "اختر صورة"), ["Result"] = ("Result", "النتيجة"),
        ["Display"] = ("Display", "العرض"), ["Sensitivity"] = ("Sensitivity", "الحساسية"),
        ["SensitivityLow"] = ("Strong differences", "فروق قوية"), ["SensitivityHigh"] = ("Subtle differences", "فروق أدق"),
        ["Opacity"] = ("Opacity", "كثافة الطبقة"), ["Original"] = ("Original", "الأصل"), ["Overlay"] = ("Overlay", "التحليل"),
        ["Default"] = ("Default", "افتراضي"), ["DefaultTip"] = ("Reset sensitivity and opacity", "إعادة الحساسية والكثافة فقط"),
        ["ShowOriginal"] = ("Show original", "إظهار الأصل"), ["HoldTip"] = ("Hold to compare. Release to return.", "اضغط للمقارنة، وأفلت للعودة."),
        ["Zoom"] = ("Zoom", "التكبير"), ["ZoomIn"] = ("Zoom in", "تكبير"), ["ZoomOut"] = ("Zoom out", "تصغير"),
        ["Fit"] = ("Fit", "ملاءمة"), ["Actual"] = ("1:1", "1:1"), ["Theme"] = ("Light / dark", "فاتح / داكن"),
        ["Language"] = ("العربية", "English"), ["Empty"] = ("Open an image to begin", "افتح صورة للبدء"),
        ["NoImage"] = ("No image", "لا توجد صورة"), ["Legend"] = ("Local differences", "اختلافات محلية"),
        ["LegendNote"] = ("Intensity, not proof of editing.", "الشدة ليست دليلاً على التلاعب."),
        ["Low"] = ("Purple · Low", "بنفسجي · أقل"), ["Medium"] = ("Teal · Medium", "تركوازي · متوسط"),
        ["High"] = ("Yellow · High", "أصفر · أعلى"), ["Navigation"] = ("Scroll to zoom · drag to pan", "مرّر للتكبير · اسحب للتحريك"),
        ["Ready"] = ("Ready", "جاهز"), ["ImageReady"] = ("Image ready", "الصورة جاهزة"),
        ["Loading"] = ("Loading image…", "جارٍ فتح الصورة…"), ["Analyzing"] = ("Analyzing…", "جارٍ التحليل…"),
        ["Done"] = ("Analysis complete", "اكتمل التحليل"), ["Saving"] = ("Saving…", "جارٍ الحفظ…"),
        ["Saved"] = ("Saved: {0}", "تم الحفظ: {0}"), ["Cancelled"] = ("Cancelled", "أُلغي التحليل"),
        ["Cancelling"] = ("Cancelling…", "جارٍ الإلغاء…"), ["Failed"] = ("Unable to complete", "تعذر إكمال العملية"),
        ["JpegOnly"] = ("JPEG image required", "تتطلب صورة JPEG"), ["NoEvidence"] = ("Limited regional evidence", "معلومات محلية محدودة"),
        ["InvalidImage"] = ("Unable to read this image", "تعذرت قراءة الصورة"), ["MissingImage"] = ("Image not found", "الصورة غير موجودة"),
        ["MemoryError"] = ("Try a smaller image", "جرّب صورة أصغر"), ["FileError"] = ("Unable to access the file", "تعذر الوصول إلى الملف"),
        ["Dismiss"] = ("Dismiss", "إغلاق"), ["AnalysisProgress"] = ("Analysis progress", "تقدم التحليل"),
        ["Combined"] = ("All signals", "جميع الفحوص"), ["CombinedResult"] = ("Combined", "النتيجة المدمجة"),
        ["Noise"] = ("Noise", "الضجيج"), ["Frequency"] = ("Frequency", "الترددات"),
        ["Compression"] = ("Compression", "الضغط"), ["Texture"] = ("Texture & edges", "النسيج والحواف"), ["Ela"] = ("ELA", "ELA"),
        ["CombinedInfo"] = ("Combined local signals. Not an authenticity verdict.", "دمج الفروق المحلية، وليس حكماً على الأصالة."),
        ["NoiseInfo"] = ("Compares local noise. Denoising and lighting also affect it.", "يقارن الضجيج المحلي، ويتأثر بالإضاءة وإزالة الضجيج."),
        ["FrequencyInfo"] = ("Compares DCT energy. Focus and patterns also affect it.", "يقارن طاقة DCT، ويتأثر بالتركيز والأنماط."),
        ["CompressionInfo"] = ("Compares JPEG blocks. Previous saves affect the result.", "يقارن بلوكات JPEG، ويتأثر بالحفظ السابق."),
        ["TextureInfo"] = ("Compares texture and edges. Natural boundaries may respond.", "يقارن النسيج والحواف، وقد يستجيب للحدود الطبيعية."),
        ["ElaInfo"] = ("JPEG recompression heuristic. Not proof of editing.", "فحص تقريبي بإعادة ضغط JPEG، وليس دليلاً على التعديل."),
        ["Masked"] = ("insufficient evidence", "معلومات غير كافية"), ["Intensity"] = ("intensity", "شدة الاختلاف")
    };
    public string this[string key] => Words.TryGetValue(key, out var value) ? arabic ? value.Ar : value.En : key;
}
