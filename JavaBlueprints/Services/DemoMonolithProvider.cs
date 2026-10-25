// ============================================================================
// ФАЙЛ: Services/DemoMonolithProvider.cs
// [МЕТОДИЧКА]: Готовый демонстрационный Java-монолит, который содержит ВСЕ
// конструкции со слайдов 4, 5, 6 и 7:
// - Консольный ввод/вывод через Scanner и String.format
// - Пользовательский класс Order, коллекции List, ArrayList, Map
// - Исключения try/catch/throw
// - Умышленный дефект O(n²) во вложенном цикле (повторная обработка одинаковых пар id),
//   который анализатор находит на лету, а оптимизатор чинит в 1 клик с ускорением в 5 раз!
// ============================================================================

namespace JavaBlueprints.Services;

public static class DemoMonolithProvider
{
    public const string DefaultMonolithJava = """
import java.util.*;
import java.io.*;
import java.nio.charset.StandardCharsets;

public class Main {
    static class Order {
        String id;
        String category;
        int amount;
        Order(String id, String category, int amount) {
            this.id = id;
            this.category = category;
            this.amount = amount;
        }
    }

    public static List<Order> processOrders(List<Order> orders) {
        List<Order> validOrders = new ArrayList<>();
        if (orders == null || orders.isEmpty()) {
            return validOrders;
        }
        for (Order order : orders) {
            if (order.id != null && !order.id.isEmpty() && order.amount > 0 && !order.category.equals("INVALID")) {
                validOrders.add(order);
            } else {
                continue;
            }
        }
        List<Order> mergedOrders = new ArrayList<>();
        List<String> processedIds = new ArrayList<>();
        // [BOTTLENECK_N2_START]
        for (Order a : validOrders) {
            if (processedIds.contains(a.id)) {
                continue;
            }
            int combinedAmount = 0;
            for (Order b : validOrders) {
                if (a.id.equals(b.id)) {
                    combinedAmount = combinedAmount + b.amount;
                }
            }
            processedIds.add(a.id);
            mergedOrders.add(new Order(a.id, a.category, combinedAmount));
        }
        // [BOTTLENECK_N2_END]
        return mergedOrders;
    }

    public static void main(String[] args) {
        try {
            Scanner scanner = new Scanner(System.in);
            if (!scanner.hasNextInt()) {
                System.out.println("COUNT=0 | TOTAL=0");
                return;
            }
            int n = scanner.nextInt();
            List<Order> inputOrders = new ArrayList<>();
            for (int i = 0; i < n; i++) {
                String id = scanner.next();
                String cat = scanner.next();
                int amount = scanner.nextInt();
                inputOrders.add(new Order(id, cat, amount));
            }
            List<Order> result = processOrders(inputOrders);
            int totalSum = 0;
            StringBuilder summary = new StringBuilder();
            for (Order ord : result) {
                totalSum = totalSum + ord.amount;
                if (result.size() <= 10) {
                    if (summary.length() > 0) {
                        summary.append(", ");
                    }
                    summary.append(String.format("%s:%s=%d", ord.id, ord.category, ord.amount));
                }
            }
            if (result.size() == 0) {
                System.out.println("COUNT=0 | TOTAL=0");
            } else if (result.size() <= 10) {
                System.out.println(summary.toString() + " | COUNT=" + result.size() + " | TOTAL=" + totalSum);
            } else {
                System.out.println("PROCESSED=" + result.size() + " | TOTAL=" + totalSum);
            }
        } catch (Exception ex) {
            System.err.println("ERROR: " + ex.getMessage());
        }
    }
}
""";
}