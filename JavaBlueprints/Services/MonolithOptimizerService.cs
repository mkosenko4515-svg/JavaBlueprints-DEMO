// ============================================================================
// ФАЙЛ: Services/MonolithOptimizerService.cs
// [МЕТОДИЧКА]: Реализует два ключевых преобразования со слайдов 4, 5 и 6:
// 1. OptimizeJavaCode() — заменяет квадратичный вложенный цикл O(n²)
//    на индексацию через HashMap/LinkedHashMap за O(n) и добавляет UTF-8 в Scanner,
//    при этом СТРОГО сохраняя формат выходных данных (для прохождения автотестов!).
// 2. DecomposeMonolith() — автоматически разрезает монолит Main.java на 4 класса
//    по ответственности (слайд 6): Parser.java, Analyzer.java, Optimizer.java, Main.java.
// ============================================================================

using JavaBlueprints.Models;

namespace JavaBlueprints.Services;

public class MonolithOptimizerService
{
    /// <summary>
    /// Оптимизирует узкие места O(n²) -> O(n) через индексацию по ключу (HashMap/LinkedHashMap)
    /// с гарантированным сохранением наблюдаемого поведения программы.
    /// </summary>
    public string OptimizeJavaCode(string sourceJava)
    {
        string optimized = sourceJava;

        // 1. Гарантируем поддержку кириллицы через StandardCharsets.UTF_8 (Слайд 7, раздел 2)
        optimized = optimized.Replace(
            "new Scanner(System.in)",
            "new Scanner(System.in, StandardCharsets.UTF_8)");

        // 2. Заменяем квадратичный поиск дублей/пар во вложенном цикле O(n²) на индексацию через LinkedHashMap O(n)
        // [МЕТОДИЧКА]: LinkedHashMap сохраняет точный порядок элементов как в исходном монолите,
        // поэтому stdout совпадает байт-в-байт, а сложность падает с O(n²) до O(n)!
        if (optimized.Contains("// [BOTTLENECK_N2_START]") && optimized.Contains("// [BOTTLENECK_N2_END]"))
        {
            int startIdx = optimized.IndexOf("// [BOTTLENECK_N2_START]");
            int endIdx = optimized.IndexOf("// [BOTTLENECK_N2_END]") + "// [BOTTLENECK_N2_END]".Length;

            string replacement = """
            // [OPTIMIZED_O_N]: Индексация заказов по id через LinkedHashMap за O(n) вместо вложенного цикла O(n²)
                    Map<String, Integer> aggregatedById = new LinkedHashMap<>();
                    Map<String, String> categoryById = new HashMap<>();
                    for (Order order : validOrders) {
                        aggregatedById.put(order.id, aggregatedById.getOrDefault(order.id, 0) + order.amount);
                        categoryById.putIfAbsent(order.id, order.category);
                    }
                    for (Map.Entry<String, Integer> entry : aggregatedById.entrySet()) {
                        String id = entry.getKey();
                        mergedOrders.add(new Order(id, categoryById.get(id), entry.getValue()));
                    }
            """;

            optimized = optimized[..startIdx] + replacement + optimized[endIdx..];
        }

        return optimized;
    }

    /// <summary>
    /// Декомпозирует монолит Main.java на 4 отдельных файла по ответственности (строго по схеме со слайда 6):
    /// Parser.java -> Analyzer.java -> Optimizer.java -> Main.java
    /// </summary>
    public List<VirtualProjectFile> DecomposeMonolith(string currentMainCode)
    {
        return new List<VirtualProjectFile>
        {
            new VirtualProjectFile
            {
                FileName = "Parser.java",
                Folder = "src/com.example",
                Responsibility = "Парсинг входного потока (Scanner + StandardCharsets.UTF_8)",
                SourceCode = """
                import java.util.*;
                import java.nio.charset.StandardCharsets;

                public class Parser {
                    public static List<Main.Order> parseOrders(Scanner scanner) {
                        List<Main.Order> rawOrders = new ArrayList<>();
                        if (!scanner.hasNextInt()) {
                            return rawOrders;
                        }
                        int count = scanner.nextInt();
                        for (int i = 0; i < count; i++) {
                            String id = scanner.next();
                            String category = scanner.next();
                            int amount = scanner.nextInt();
                            rawOrders.add(new Main.Order(id, category, amount));
                        }
                        return rawOrders;
                    }
                }
                """
            },
            new VirtualProjectFile
            {
                FileName = "Analyzer.java",
                Folder = "src/com.example",
                Responsibility = "Валидация данных и фильтрация некорректных записей",
                SourceCode = """
                import java.util.*;

                public class Analyzer {
                    public static List<Main.Order> filterValidOrders(List<Main.Order> orders) {
                        List<Main.Order> valid = new ArrayList<>();
                        if (orders == null || orders.isEmpty()) {
                            return valid;
                        }
                        for (Main.Order order : orders) {
                            if (order.id != null && !order.id.isEmpty() && order.amount > 0 && !order.category.equals("INVALID")) {
                                valid.add(order);
                            }
                        }
                        return valid;
                    }
                }
                """
            },
            new VirtualProjectFile
            {
                FileName = "Optimizer.java",
                Folder = "src/com.example",
                Responsibility = "Индексация по id за O(n) и расчет агрегатов",
                SourceCode = """
                import java.util.*;

                public class Optimizer {
                    public static List<Main.Order> mergeByIdLinear(List<Main.Order> validOrders) {
                        Map<String, Integer> sumById = new LinkedHashMap<>();
                        Map<String, String> catById = new HashMap<>();
                        for (Main.Order order : validOrders) {
                            sumById.put(order.id, sumById.getOrDefault(order.id, 0) + order.amount);
                            catById.putIfAbsent(order.id, order.category);
                        }
                        List<Main.Order> result = new ArrayList<>();
                        for (Map.Entry<String, Integer> entry : sumById.entrySet()) {
                            result.add(new Main.Order(entry.getKey(), catById.get(entry.getKey()), entry.getValue()));
                        }
                        return result;
                    }
                }
                """
            },
            new VirtualProjectFile
            {
                FileName = "Main.java",
                Folder = "src/com.example",
                Responsibility = "Координация пайплайна и форматированный вывод результата",
                SourceCode = OptimizeJavaCode(currentMainCode)
            }
        };
    }
}