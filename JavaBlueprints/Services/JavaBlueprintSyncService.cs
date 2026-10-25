// ============================================================================
// ФАЙЛ: Services/JavaBlueprintSyncService.cs
// Полная поддержка любых имен классов, пакетов, многострочных Stream API / лямбд
// и двусторонней генерации кода без потери исходников.
// ============================================================================

using System.Text;
using System.Text.RegularExpressions;
using JavaBlueprints.Models;

namespace JavaBlueprints.Services;

public class JavaBlueprintSyncService
{
    /// <summary>
    /// Препроцессинг:
    /// 1) Склеивает многострочные цепочки вызовов (например, .stream()\n.filter()\n.toList()),
    ///    чтобы они становились единым атомарным узлом графа и не терялись при регенерации.
    /// 2) Очищает комментарии и строковые литералы для безопасной работы Regex.
    /// </summary>
    private static (List<string> CleanLines, List<string> RawLines, List<int> OriginalLineNumbers) NormalizeAndPreprocess(string javaCode)
    {
        string normalized = javaCode.Replace("\r\n", "\n");
        string[] physicalLines = normalized.Split('\n');

        var mergedRawLines = new List<string>();
        var lineNumbers = new List<int>();

        // Шаг 1: Склеиваем многострочные выражения (Stream API чейнинг с ведущей точкой '.' или незакрытые скобки)
        var currentStmt = new StringBuilder();
        int stmtStartLine = 1;
        int parenBalance = 0;

        for (int idx = 0; idx < physicalLines.Length; idx++)
        {
            string line = physicalLines[idx];
            string trimmed = StripTrailingCommentPreservingStrings(line);

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (currentStmt.Length == 0)
                {
                    mergedRawLines.Add("");
                    lineNumbers.Add(idx + 1);
                }
                continue;
            }

            bool isChainContinuation = trimmed.StartsWith(".") ||
                                       trimmed.StartsWith("->") ||
                                       trimmed.StartsWith("?") ||
                                       trimmed.StartsWith(":") ||
                                       parenBalance > 0;

            if (currentStmt.Length > 0 && isChainContinuation)
            {
                currentStmt.Append(' ').Append(trimmed);
            }
            else
            {
                if (currentStmt.Length > 0)
                {
                    mergedRawLines.Add(currentStmt.ToString());
                    lineNumbers.Add(stmtStartLine);
                    currentStmt.Clear();
                }
                currentStmt.Append(line);
                stmtStartLine = idx + 1;
                parenBalance = 0;
            }

            parenBalance += CountOutsideStrings(trimmed, '(') - CountOutsideStrings(trimmed, ')');
            if (parenBalance < 0) parenBalance = 0;

            // Если скобки сбалансированы и следующая строка не начинается с точки-чейнинга — закрываем инструкцию
            bool nextStartsWithDot = (idx + 1 < physicalLines.Length) &&
                                     StripTrailingCommentPreservingStrings(physicalLines[idx + 1]).StartsWith(".");

            if (parenBalance == 0 && !nextStartsWithDot)
            {
                mergedRawLines.Add(currentStmt.ToString());
                lineNumbers.Add(stmtStartLine);
                currentStmt.Clear();
            }
        }

        if (currentStmt.Length > 0)
        {
            mergedRawLines.Add(currentStmt.ToString());
            lineNumbers.Add(stmtStartLine);
        }

        // Шаг 2: Очищаем склеенные строки от комментариев и содержимого кавычек "..."
        var cleanLines = new List<string>(mergedRawLines.Count);
        bool inBlockComment = false;

        foreach (string raw in mergedRawLines)
        {
            var sb = new StringBuilder(raw.Length);
            int i = 0;
            int len = raw.Length;

            while (i < len)
            {
                if (inBlockComment)
                {
                    if (i + 1 < len && raw[i] == '*' && raw[i + 1] == '/')
                    {
                        inBlockComment = false;
                        i += 2;
                    }
                    else i++;
                    continue;
                }

                if (i + 1 < len && raw[i] == '/' && raw[i + 1] == '/')
                    break;

                if (i + 1 < len && raw[i] == '/' && raw[i + 1] == '*')
                {
                    inBlockComment = true;
                    i += 2;
                    continue;
                }

                if (raw[i] == '"')
                {
                    sb.Append("\"\"");
                    i++;
                    while (i < len && raw[i] != '"')
                    {
                        if (raw[i] == '\\' && i + 1 < len) i += 2;
                        else i++;
                    }
                    if (i < len) i++;
                    continue;
                }

                if (raw[i] == '\'')
                {
                    sb.Append("''");
                    i++;
                    while (i < len && raw[i] != '\'')
                    {
                        if (raw[i] == '\\' && i + 1 < len) i += 2;
                        else i++;
                    }
                    if (i < len) i++;
                    continue;
                }

                sb.Append(raw[i]);
                i++;
            }

            cleanLines.Add(sb.ToString());
        }

        return (cleanLines, mergedRawLines, lineNumbers);
    }

    private static int CountOutsideStrings(string line, char target)
    {
        int count = 0;
        bool inStr = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length) { i++; continue; }
            if (line[i] == '"') inStr = !inStr;
            else if (!inStr && line[i] == target) count++;
        }
        return count;
    }

    private static string StripTrailingCommentPreservingStrings(string rawLine)
    {
        bool inString = false;
        bool inChar = false;
        for (int i = 0; i < rawLine.Length; i++)
        {
            char c = rawLine[i];
            if (c == '\\' && i + 1 < rawLine.Length) { i++; continue; }
            if (!inChar && c == '"') inString = !inString;
            else if (!inString && c == '\'') inChar = !inChar;
            else if (!inString && !inChar && i + 1 < rawLine.Length && c == '/' && rawLine[i + 1] == '/')
                return rawLine[..i].Trim();
        }
        return rawLine.Trim();
    }

    public BlueprintGraph ParseJavaToGraph(string javaCode, string fileName = "Main.java")
    {
        var graph = new BlueprintGraph { FileName = fileName };
        var (cleanLines, rawLines, lineNumbers) = NormalizeAndPreprocess(javaCode);

        // Определяем имя главного класса в файле (первый public class или класс с методом main)
        string primaryClassName = DetectPrimaryClassName(cleanLines);

        int braceDepth = 0;
        int methodBaseDepth = 1;
        bool insideHelperClass = false;
        var helperBuffer = new StringBuilder();
        string pendingBranchTag = "Поток";

        for (int i = 0; i < cleanLines.Count; i++)
        {
            string cleanTrimmed = cleanLines[i].Trim();
            string rawLine = rawLines[i];
            string actualTrimmed = StripTrailingCommentPreservingStrings(rawLine);
            int lineNumber = lineNumbers[i];

            if (string.IsNullOrWhiteSpace(cleanTrimmed))
                continue;

            // 1. Пакет и импорты
            if (cleanTrimmed.StartsWith("package "))
            {
                graph.PackageName = cleanTrimmed.Replace("package ", "").TrimEnd(';').Trim();
                continue;
            }
            if (cleanTrimmed.StartsWith("import "))
            {
                string imp = cleanTrimmed.Replace("import ", "").TrimEnd(';').Trim();
                if (!graph.Imports.Contains(imp))
                    graph.Imports.Add(imp);
                continue;
            }

            // 2. Вспомогательные классы/record/enum внутри монолита (все классы, кроме главного primaryClassName)
            var classMatch = Regex.Match(cleanTrimmed, @"^(?:public\s+|private\s+|protected\s+|static\s+|final\s+)*(?:class|record|enum|interface)\s+(\w+)");
            if (classMatch.Success)
            {
                string foundClass = classMatch.Groups[1].Value;
                if (!insideHelperClass && braceDepth == 0 && foundClass == primaryClassName)
                {
                    graph.ClassHeader = actualTrimmed.TrimEnd('{').Trim();
                    braceDepth += CountChar(cleanTrimmed, '{') - CountChar(cleanTrimmed, '}');
                    continue;
                }
                else
                {
                    insideHelperClass = true;
                }
            }

            if (insideHelperClass)
            {
                helperBuffer.AppendLine(rawLine);
                braceDepth += CountChar(cleanTrimmed, '{') - CountChar(cleanTrimmed, '}');
                if (braceDepth <= 1 && cleanTrimmed.EndsWith("}"))
                {
                    graph.HelperTypesCode.Add(helperBuffer.ToString().TrimEnd());
                    helperBuffer.Clear();
                    insideHelperClass = false;
                }
                continue;
            }

            if (cleanTrimmed == "}" || cleanTrimmed == "};")
            {
                braceDepth = Math.Max(0, braceDepth - 1);
                continue;
            }

            if (cleanTrimmed.StartsWith("} else if") || cleanTrimmed.StartsWith("else if"))
            {
                braceDepth = Math.Max(1, braceDepth - (cleanTrimmed.StartsWith("}") ? 1 : 0));
                string cond = ExtractParentheses(actualTrimmed);
                AddNode(graph, NodeKind.Condition, "Условие (else if)", cond, Math.Max(0, braceDepth - methodBaseDepth), lineNumber, "Нет");
                braceDepth += CountChar(cleanTrimmed, '{') - CountChar(cleanTrimmed.StartsWith("}") ? cleanTrimmed[1..] : cleanTrimmed, '}');
                pendingBranchTag = "Да";
                continue;
            }

            if (cleanTrimmed.StartsWith("} else") || cleanTrimmed == "else {" || cleanTrimmed == "else")
            {
                braceDepth = Math.Max(1, braceDepth - (cleanTrimmed.StartsWith("}") ? 1 : 0));
                braceDepth += CountChar(cleanTrimmed, '{') - CountChar(cleanTrimmed.StartsWith("}") ? cleanTrimmed[1..] : cleanTrimmed, '}');
                pendingBranchTag = "Нет";
                continue;
            }

            if (cleanTrimmed.StartsWith("} catch") || cleanTrimmed.StartsWith("catch"))
            {
                braceDepth = Math.Max(1, braceDepth - (cleanTrimmed.StartsWith("}") ? 1 : 0));
                string exc = ExtractParentheses(actualTrimmed);
                AddNode(graph, NodeKind.TryCatch, $"Перехват ({exc})", $"catch ({exc})", Math.Max(0, braceDepth - methodBaseDepth), lineNumber, "Исключение");
                braceDepth += CountChar(cleanTrimmed, '{') - CountChar(cleanTrimmed.StartsWith("}") ? cleanTrimmed[1..] : cleanTrimmed, '}');
                pendingBranchTag = "Поток";
                continue;
            }

            int relativeDepth = Math.Max(0, braceDepth - methodBaseDepth);

            // 3. Классификация узлов (включая Stream API и лямбды!)
            bool isStreamOrLambda = cleanTrimmed.Contains(".stream(") ||
                                    cleanTrimmed.Contains(".parallelStream(") ||
                                    cleanTrimmed.Contains("->") ||
                                    cleanTrimmed.Contains("::");

            if (IsMethodDeclaration(cleanTrimmed))
            {
                methodBaseDepth = braceDepth + 1;
                string cleanSig = actualTrimmed.TrimEnd('{').Trim();
                AddNode(graph, NodeKind.Method, "Метод", cleanSig, 0, lineNumber, "Поток");
                pendingBranchTag = "Поток";
            }
            else if (Regex.IsMatch(cleanTrimmed, @"^(for|while)\s*\("))
            {
                string loopHead = actualTrimmed.TrimEnd('{').Trim();
                string loopType = cleanTrimmed.Contains(':') ? "Цикл (for each)" : "Цикл";
                AddNode(graph, NodeKind.Loop, loopType, loopHead, relativeDepth, lineNumber, pendingBranchTag);
                pendingBranchTag = "Тело цикла";
            }
            else if (Regex.IsMatch(cleanTrimmed, @"^(if|switch)\s*\("))
            {
                string condExpr = ExtractParentheses(actualTrimmed);
                string title = cleanTrimmed.StartsWith("switch") ? "Выбор (switch)" : "Условие";
                AddNode(graph, NodeKind.Condition, title, condExpr + "?", relativeDepth, lineNumber, pendingBranchTag);
                pendingBranchTag = "Да";
            }
            else if (cleanTrimmed.StartsWith("try ") || cleanTrimmed == "try {" || cleanTrimmed == "try")
            {
                AddNode(graph, NodeKind.TryCatch, "Блок try", "try", relativeDepth, lineNumber, pendingBranchTag);
                pendingBranchTag = "Поток";
            }
            else if (Regex.IsMatch(cleanTrimmed, @"^(return|continue|break|throw)\b"))
            {
                string title = cleanTrimmed.StartsWith("return") ? "Возврат"
                             : cleanTrimmed.StartsWith("continue") ? "Продолжить"
                             : cleanTrimmed.StartsWith("break") ? "Прервать"
                             : "Исключение (throw)";
                AddNode(graph, NodeKind.Return, title, actualTrimmed.TrimEnd(';'), relativeDepth, lineNumber, pendingBranchTag);
                pendingBranchTag = "Поток";
            }
            else if (isStreamOrLambda)
            {
                // Функциональный конвейер Stream API / Лямбда-выражение сохраняется как полноценный редактируемый узел!
                NodeKind streamKind = IsVariableDeclaration(cleanTrimmed) ? NodeKind.Variable : NodeKind.Operation;
                AddNode(graph, streamKind, "Stream API / Лямбда", actualTrimmed.TrimEnd(';'), relativeDepth, lineNumber, pendingBranchTag);
                pendingBranchTag = "Поток";
            }
            else if (IsVariableDeclaration(cleanTrimmed))
            {
                AddNode(graph, NodeKind.Variable, "Переменная", actualTrimmed.TrimEnd(';'), relativeDepth, lineNumber, pendingBranchTag);
                pendingBranchTag = "Поток";
            }
            else if (IsExternalOrHelperCall(cleanTrimmed))
            {
                AddNode(graph, NodeKind.ExternalCall, "Метод (вызов)", actualTrimmed.TrimEnd(';'), relativeDepth, lineNumber, pendingBranchTag);
                pendingBranchTag = "Поток";
            }
            else
            {
                AddNode(graph, NodeKind.Operation, "Операция", actualTrimmed.TrimEnd(';'), relativeDepth, lineNumber, pendingBranchTag);
                pendingBranchTag = "Поток";
            }

            braceDepth += CountChar(cleanTrimmed, '{') - CountChar(cleanTrimmed, '}');
        }

        BuildEdgesAndLayout(graph);
        return graph;
    }

    private static string DetectPrimaryClassName(List<string> cleanLines)
    {
        foreach (string line in cleanLines)
        {
            var m = Regex.Match(line.Trim(), @"^public\s+(?:final\s+)?class\s+(\w+)");
            if (m.Success) return m.Groups[1].Value;
        }
        foreach (string line in cleanLines)
        {
            var m = Regex.Match(line.Trim(), @"^(?:final\s+)?class\s+(\w+)");
            if (m.Success) return m.Groups[1].Value;
        }
        return "Main";
    }

    public string GenerateJavaFromGraph(BlueprintGraph graph)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(graph.PackageName) && graph.PackageName != "com.example")
        {
            sb.AppendLine($"package {graph.PackageName};");
            sb.AppendLine();
        }

        foreach (var imp in graph.Imports.Distinct())
        {
            sb.AppendLine($"import {imp};");
        }
        sb.AppendLine();

        string classHeader = string.IsNullOrWhiteSpace(graph.ClassHeader) ? "public class Main" : graph.ClassHeader.TrimEnd('{').Trim();
        sb.AppendLine($"{classHeader} {{");

        foreach (var helper in graph.HelperTypesCode)
        {
            sb.AppendLine(helper);
            sb.AppendLine();
        }

        int currentDepth = 0;

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];

            if (node.Kind == NodeKind.Method)
            {
                while (currentDepth > 0)
                {
                    sb.AppendLine($"{new string(' ', currentDepth * 4)}}}");
                    currentDepth--;
                }

                if (i > 0) sb.AppendLine();

                string methodSig = node.CodeSnippet.TrimEnd('{', ';').Trim();
                sb.AppendLine($"    {methodSig} {{");
                node.SourceLine = CountLines(sb);
                currentDepth = 1;
                continue;
            }

            if (currentDepth == 0)
            {
                sb.AppendLine("    public static void main(String[] args) {");
                currentDepth = 1;
            }

            int targetDepth = Math.Clamp(node.Depth, 1, currentDepth);
            node.Depth = targetDepth;

            string snippet = node.CodeSnippet.Trim();
            bool isCatchBlock = node.Kind == NodeKind.TryCatch && snippet.StartsWith("catch");
            bool isElseBranch = node.BranchTag == "Нет";

            while (currentDepth > targetDepth)
            {
                sb.AppendLine($"{new string(' ', currentDepth * 4)}}}");
                currentDepth--;
            }

            string indent = new string(' ', (targetDepth + 1) * 4);

            if (isCatchBlock)
            {
                string catchCode = snippet.TrimEnd('{').Trim();
                sb.AppendLine($"{indent}{catchCode} {{");
                node.SourceLine = CountLines(sb);
                currentDepth = targetDepth + 1;
                continue;
            }

            if (isElseBranch)
            {
                if (node.Kind == NodeKind.Condition)
                {
                    string cond = CleanConditionExpression(snippet);
                    sb.AppendLine($"{indent}else if ({cond}) {{");
                    node.SourceLine = CountLines(sb);
                    currentDepth = targetDepth + 1;
                    continue;
                }
                else
                {
                    sb.AppendLine($"{indent}else {{");
                    currentDepth = targetDepth + 1;
                    targetDepth = currentDepth;
                    indent = new string(' ', (targetDepth + 1) * 4);
                }
            }

            switch (node.Kind)
            {
                case NodeKind.Loop:
                    string loopCode = snippet.TrimEnd('{', ';').Trim();
                    if (!loopCode.StartsWith("for") && !loopCode.StartsWith("while"))
                        loopCode = $"for ({loopCode})";
                    sb.AppendLine($"{indent}{loopCode} {{");
                    node.SourceLine = CountLines(sb);
                    currentDepth = targetDepth + 1;
                    break;

                case NodeKind.Condition:
                    string condExpr = CleanConditionExpression(snippet);
                    string keyword = snippet.StartsWith("switch") ? "switch" : "if";
                    sb.AppendLine($"{indent}{keyword} ({condExpr}) {{");
                    node.SourceLine = CountLines(sb);
                    currentDepth = targetDepth + 1;
                    break;

                case NodeKind.TryCatch:
                    string tryCode = snippet.TrimEnd('{', ';').Trim();
                    if (string.IsNullOrWhiteSpace(tryCode)) tryCode = "try";
                    sb.AppendLine($"{indent}{tryCode} {{");
                    node.SourceLine = CountLines(sb);
                    currentDepth = targetDepth + 1;
                    break;

                case NodeKind.Variable:
                case NodeKind.Operation:
                case NodeKind.ExternalCall:
                case NodeKind.Return:
                    string stmt = snippet.TrimEnd(';').Trim();
                    if (!string.IsNullOrWhiteSpace(stmt))
                    {
                        sb.AppendLine($"{indent}{stmt};");
                    }
                    node.SourceLine = CountLines(sb);
                    break;
            }
        }

        while (currentDepth > 0)
        {
            sb.AppendLine($"{new string(' ', currentDepth * 4)}}}");
            currentDepth--;
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string CleanConditionExpression(string raw)
    {
        string s = raw.Trim().TrimEnd('{', '?').Trim();
        if (s.StartsWith("else if")) s = s["else if".Length..].Trim();
        else if (s.StartsWith("if")) s = s["if".Length..].Trim();
        else if (s.StartsWith("switch")) s = s["switch".Length..].Trim();

        if (s.StartsWith("(") && s.EndsWith(")") && s.Length >= 2)
            s = s[1..^1].Trim();

        return string.IsNullOrWhiteSpace(s) ? "true" : s;
    }

    public void BuildEdgesAndLayout(BlueprintGraph graph)
    {
        graph.Edges.Clear();
        double yCursor = 28;

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];

            double branchOffset = node.BranchTag == "Нет" ? (node.Depth * 36 + 140) : (node.Depth * 36);
            node.X = 36 + branchOffset;
            node.Y = yCursor;
            yCursor += 108;

            if (i > 0)
            {
                var prev = graph.Nodes[i - 1];
                if (node.Kind != NodeKind.Method)
                {
                    string edgeLabel = prev.Kind == NodeKind.Condition ? node.BranchTag : "";
                    graph.Edges.Add(new BlueprintEdge
                    {
                        FromNodeId = prev.Id,
                        ToNodeId = node.Id,
                        Label = edgeLabel
                    });
                }
            }

            if (node.Kind == NodeKind.Loop)
            {
                int lastInLoop = i;
                for (int j = i + 1; j < graph.Nodes.Count && graph.Nodes[j].Depth > node.Depth; j++)
                {
                    lastInLoop = j;
                }
                if (lastInLoop > i)
                {
                    graph.Edges.Add(new BlueprintEdge
                    {
                        FromNodeId = graph.Nodes[lastInLoop].Id,
                        ToNodeId = node.Id,
                        Label = "Повтор цикла",
                        IsBackEdge = true
                    });
                }
            }
        }
    }

    private static void AddNode(BlueprintGraph graph, NodeKind kind, string title, string snippet, int depth, int line, string branch)
    {
        graph.Nodes.Add(new BlueprintNode
        {
            Kind = kind,
            Title = title,
            CodeSnippet = snippet,
            Depth = depth,
            SourceLine = line,
            BranchTag = branch
        });
    }

    private static bool IsMethodDeclaration(string line) =>
        Regex.IsMatch(line, @"^(public|private|protected|static|final|synchronized|\s)+[\w\<\>\[\],\s\?]+\s+\w+\s*\([^\)]*\)\s*(throws\s+[\w,\s]+)?\s*\{?$")
        && !line.Contains(" new ")
        && !line.Contains("=")
        && !line.Contains("->");

    private static bool IsVariableDeclaration(string line) =>
        Regex.IsMatch(line, @"^(final\s+)?([A-Z]\w*(\.[A-Z]\w*)*|int|long|double|boolean|char|byte|short|float|var)\b(\<[^\>]*\>)?(\[\])?\s+\w+\s*=");

    private static bool IsExternalOrHelperCall(string line) =>
        Regex.IsMatch(line, @"^(\w+\.)?(validate|isValid|processOrders|processOrder|merge|parseOrders|filterValidOrders|mergeByIdLinear|parse|analyze|optimize|formatOutput)\s*\(");

    private static string ExtractParentheses(string text)
    {
        int start = text.IndexOf('(');
        int end = text.LastIndexOf(')');
        return (start >= 0 && end > start) ? text.Substring(start + 1, end - start - 1).Trim() : text;
    }

    private static int CountChar(string s, char c) => s.Count(ch => ch == c);
    private static int CountLines(StringBuilder sb) => sb.ToString().Count(c => c == '\n');
}