

// ============================================================================
// ФАЙЛ: Models/BlueprintModels.cs
// [МЕТОДИЧКА]: Сердце архитектуры. Чтобы синхронизация "Код <-> Граф" не ломалась,
// и редактор кода, и визуальный канвас работают с ОДНОЙ структурой BlueprintGraph.
// Каждый узел (BlueprintNode) хранит ссылку на строку кода (SourceLine) и список
// привязанных к нему дефектов (Diagnostics), чтобы рисовать иконку "⚠️" прямо на блоке.
// ============================================================================

namespace JavaBlueprints.Models;

/// <summary>
/// Типы блоков визуального языка (в точности соответствуют палитре слева на слайде 4).
/// </summary>
public enum NodeKind
{
    Method,         // Фиолетовый блок: объявление метода (public List<Order> processOrders...)
    Variable,       // Синий блок: объявление/инициализация переменной или коллекции
    Condition,      // Оранжевый ромб/блок: условие if / switch с ветками "Да" и "Нет"
    Loop,           // Фиолетово-синий блок: цикл for, foreach, while
    Operation,      // Бирюзовый блок: обычная инструкция, присваивание, вызов коллекции
    ExternalCall,   // Вызов другого метода или внешнего сервиса (validator.isValid, merge)
    TryCatch,       // Блок обработки исключений try / catch / finally (Раздел 4 Java Scope)
    Return          // Зеленый терминальный блок: return / break / continue / throw
}

/// <summary>
/// Уровень критичности дефекта для правой панели "Анализ -> Проблемы" (слайд 4 и 5).
/// </summary>
public enum SeverityLevel
{
    High,           // Высокая (красный бейдж, например: O(n²) или длинный метод)
    Medium,         // Средняя (оранжевый бейдж, например: дублирование логики)
    Low             // Низкая (голубой бейдж, рекомендация по стилю или кодировке)
}

/// <summary>
/// Узел визуальной блок-схемы (Blueprint Node).
/// </summary>
public class BlueprintNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public NodeKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Выражение или строка Java-кода внутри блока.
    /// [МЕТОДИЧКА]: При редактировании этого поля в UI автоматически вызывается
    /// JavaBlueprintSyncService.GenerateJavaFromGraph(), обновляя весь Java-файл!
    /// </summary>
    public string CodeSnippet { get; set; } = string.Empty;

    /// <summary>
    /// Уровень вложенности блока в AST (0 = метод, 1 = тело метода, 2 = внутри цикла/if и т.д.).
    /// Используется для: 1) генерации отступов и закрывающих скобок '}', 2) подсчёта вложенности циклов Big O.
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Номер строки в исходном .java файле для подсветки и навигации по клику на проблему.
    /// </summary>
    public int SourceLine { get; set; }

    /// <summary>
    /// Ветка, к которой принадлежит узел ("Да" / "Нет" / "Тело цикла" / "Поток").
    /// </summary>
    public string BranchTag { get; set; } = "Поток";

    /// <summary>
    /// Координаты на канвасе (вычисляются авто-лейаутом при построении графа).
    /// </summary>
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>
    /// Привязанные к этому узлу предупреждения анализатора (отображаются как ⚠️ на узле).
    /// </summary>
    public List<CodeDiagnostic> AttachedDiagnostics { get; set; } = new();
}

/// <summary>
/// Направленное ребро графа между двумя блоками.
/// </summary>
public class BlueprintEdge
{
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty; // Например: "", "Да", "Нет", "Цикл"
    public bool IsBackEdge { get; set; }              // Обратная дуга цикла
}

/// <summary>
/// Полная модель визуального графа для одного класса/файла.
/// </summary>
public class BlueprintGraph
{
    public string FileName { get; set; } = "Main.java";
    public string PackageName { get; set; } = "com.example";
    public List<string> Imports { get; set; } = new()
    {
        "java.util.*",
        "java.io.*",
        "java.nio.charset.StandardCharsets"
    };
    public string ClassHeader { get; set; } = "public class Main";
    public List<string> HelperTypesCode { get; set; } = new();
    public List<BlueprintNode> Nodes { get; set; } = new();
    public List<BlueprintEdge> Edges { get; set; } = new();
}

/// <summary>
/// Диагностическое сообщение (дефект + риск + рекомендация — строго по слайду 5).
/// </summary>
public class CodeDiagnostic
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];
    public string RuleCode { get; set; } = string.Empty;     // Например: "BIG_O_N2", "LONG_METHOD", "DUP_LOGIC"
    public string Title { get; set; } = string.Empty;        // "Вложенность O(n²)"
    public SeverityLevel Severity { get; set; }
    public string FileName { get; set; } = "Main.java";
    public int LineNumber { get; set; }
    public string TargetNodeId { get; set; } = string.Empty;

    // 4 обязательных поля со слайда 5 ("Что видит участник"):
    public string DefectDescription { get; set; } = string.Empty; // Дефект: Повторная обработка одинаковых пар
    public string RiskDescription { get; set; } = string.Empty;   // Риск: Рост времени при увеличении входа
    public string Recommendation { get; set; } = string.Empty;    // Рекомендация: Индексировать элементы по id
    public string ExpectedEffect { get; set; } = string.Empty;    // Ожидаемый эффект: O(n²) -> O(n), до 5.2x быстрее
    public string ComplexityBadge { get; set; } = "O(n)";
    public bool CanAutoFix { get; set; }
}

/// <summary>
/// Сводка по сложности и метрикам кода (правая нижняя панель со слайда 4).
/// </summary>
public class ComplexitySummary
{
    public string CurrentBigO { get; set; } = "O(n)";
    public string OptimizedBigO { get; set; } = "O(n)";
    public string ExpectedSpeedup { get; set; } = "1.0x";
    public int TotalLines { get; set; }
    public int MaxLoopNesting { get; set; }
    public int CyclomaticComplexity { get; set; }
    public int MethodsCount { get; set; }
}

/// <summary>
/// Виртуальный файл проекта (для дерева файлов слева и для декомпозиции монолита со слайда 6).
/// </summary>
public class VirtualProjectFile
{
    public string FileName { get; set; } = string.Empty;
    public string Folder { get; set; } = "src/com.example";
    public string Responsibility { get; set; } = string.Empty; // парсинг / анализ / оптимизация / вывод
    public string SourceCode { get; set; } = string.Empty;
}

/// <summary>
/// Результат одного автоматического теста сравнения поведения (слайд 4 — "Результат тестов").
/// </summary>
public class BehavioralTestCaseResult
{
    public string TestName { get; set; } = string.Empty;
    public string InputDescription { get; set; } = string.Empty;
    public string StdinPayload { get; set; } = string.Empty;
    public string BaselineStdout { get; set; } = string.Empty;
    public string CandidateStdout { get; set; } = string.Empty;
    public long BaselineTimeMs { get; set; }
    public long CandidateTimeMs { get; set; }
    public bool BehaviorMatches =>
        Normalize(BaselineStdout) == Normalize(CandidateStdout);

    private static string Normalize(string s) =>
        string.Join("\n", (s ?? "").Replace("\r\n", "\n").Trim().Split('\n').Select(l => l.TrimEnd()));
}

/// <summary>
/// Итоговый отчет Quality Gate (Сборка + автотесты = поведение сохранено).
/// </summary>
public class QualityGateReport
{
    public bool CompilationSucceeded { get; set; }
    public bool UsedRealJavac { get; set; }
    public string CompilerOutput { get; set; } = string.Empty;
    public List<BehavioralTestCaseResult> TestResults { get; set; } = new();
    public List<string> ConsoleLogs { get; set; } = new();

    public int PassedCount => TestResults.Count(t => t.BehaviorMatches);
    public int TotalCount => TestResults.Count;
    public bool AllBehaviorPreserved => CompilationSucceeded && TotalCount > 0 && PassedCount == TotalCount;

    public double ActualSpeedupFactor
    {
        get
        {
            var largeTest = TestResults.LastOrDefault();
            if (largeTest == null || largeTest.CandidateTimeMs <= 0) return 1.0;
            return Math.Round((double)Math.Max(1, largeTest.BaselineTimeMs) / largeTest.CandidateTimeMs, 1);
        }
    }
}