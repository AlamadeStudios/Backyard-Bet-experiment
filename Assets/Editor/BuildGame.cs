using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Сборка игры в отдельную папку - чтобы разослать друзьям и играть без
/// редактора.
///
/// Собирается пунктом меню или из командной строки:
///     Unity.exe -batchmode -quit -projectPath &lt;проект&gt;
///               -executeMethod BuildGame.FromCommandLine
///
/// Папка сборки лежит рядом с проектом, а не внутри него: всё, что внутри
/// Assets, Unity импортирует как ассеты, и собранная игра попала бы сама в
/// себя при следующей сборке.
/// </summary>
public static class BuildGame
{
    const string Product = "BackyardBet";
    static readonly string DefaultDir =
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "BackyardBet-Build"));

    [MenuItem("Backyard Bet/Собрать игру (Windows exe)")]
    public static void FromMenu()
    {
        string dir = Build(DefaultDir);
        if (dir != null) EditorUtility.RevealInFinder(dir);
    }

    /// <summary>Точка входа для batchmode. Путь можно задать через -buildOut.</summary>
    public static void FromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        string dir = DefaultDir;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-buildOut") dir = args[i + 1];

        if (Build(dir) == null) EditorApplication.Exit(1);
    }

    static string Build(string dir)
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[Backyard Bet] В списке сборки нет сцен - собирать нечего.");
            return null;
        }

        Directory.CreateDirectory(dir);

        // Полноэкранное окно, а не эксклюзивный полный экран: из окна проще
        // выйти и проще открыть второе на той же машине для проверки вдвоём.
        PlayerSettings.productName = Product;
        PlayerSettings.companyName = "Alameda Studios";
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.runInBackground = true;      // хост не должен засыпать без фокуса
        PlayerSettings.resizableWindow = true;

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(dir, Product + ".exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        Debug.Log("[Backyard Bet] Собираю в " + dir + ", сцен: " + scenes.Length);
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary s = report.summary;

        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError("[Backyard Bet] Сборка не удалась: " + s.result +
                           ", ошибок " + s.totalErrors);
            return null;
        }

        WriteReadme(dir);
        Debug.Log(string.Format(
            "[Backyard Bet] Сборка готова: {0}  ({1:N1} МБ, {2:N0} с)",
            options.locationPathName, s.totalSize / 1024f / 1024f,
            s.totalTime.TotalSeconds));
        return dir;
    }

    /// <summary>
    /// Записка рядом с игрой: как соединиться. Без неё друзьям придётся
    /// пересказывать всё голосом, а про порт UDP всё равно забудут.
    /// </summary>
    static void WriteReadme(string dir)
    {
        string text = string.Join(Environment.NewLine, new[]
        {
            "BACKYARD BET — как играть вместе",
            "",
            "1. Кто-то один нажимает ОТКРЫТЬ КОМНАТУ. В его окне появится",
            "   адрес и порт — он их диктует остальным.",
            "2. Остальные вводят этот адрес и порт и жмут ПОДКЛЮЧИТЬСЯ.",
            "",
            "В одной сети (общий Wi-Fi или кабель) это работает сразу.",
            "",
            "Через интернет нужно одно из двух:",
            "  • хост пробрасывает на роутере порт 7777 UDP на свой компьютер",
            "    и диктует друзьям свой внешний адрес;",
            "  • либо все ставят общую VPN (Radmin VPN, ZeroTier, Hamachi) и",
            "    подключаются по адресу, который выдала VPN.",
            "",
            "Если не соединяется:",
            "  • брандмауэр Windows при первом запуске спросит разрешение —",
            "    нужно разрешить для частной сети;",
            "  • порт должен совпадать у хоста и у подключающихся;",
            "  • хост не должен сворачиваться в спящий режим.",
            "",
            "Управление: WASD — идти, Space — прыжок, E — взять или открыть,",
            "ЛКМ удерживать — замах и бросок, R — отпить из бутылки,",
            "Esc — курсор и выход из-за стола.",
        });
        File.WriteAllText(Path.Combine(dir, "КАК ИГРАТЬ.txt"), text);
    }
}
