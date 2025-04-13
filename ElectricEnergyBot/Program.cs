using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using HtmlAgilityPack;
using Newtonsoft.Json;

class Program
{
    private static TelegramBotClient botClient;
    private static HttpClient httpClient = new HttpClient();
    private static Dictionary<long, UserState> userStates = new Dictionary<long, UserState>();
    private static Dictionary<string, string> electronicsDictionary = new Dictionary<string, string>();
    private static Dictionary<string, string> colorCodeTable = new Dictionary<string, string>();

    private class UserState
    {
        public string CurrentMenu { get; set; }
        public string PreviousMenu { get; set; }
        public string CurrentCalculation { get; set; }
        public Dictionary<string, string> CalculationValues { get; set; } = new Dictionary<string, string>();
        public List<double> SeriesResistors { get; set; } = new List<double>();
        public List<double> ParallelResistors { get; set; } = new List<double>();
    }

    static async Task Main(string[] args)
    {
        InitializeDictionaries();
        
        string botToken = "7718297524:AAFd_Zi8D6q4ZGKHyCmb1pweJETVgotqIcU";
        botClient = new TelegramBotClient(botToken);

        var me = await botClient.GetMeAsync();
        Console.WriteLine($"Bot başlatıldı: {me.Username}");

        botClient.StartReceiving(UpdateHandler, ErrorHandler);
        Console.WriteLine("Bot dinlemede...");
        Console.ReadLine();
    }

    private static void InitializeDictionaries()
    {
        // Elektronik Terimler Sözlüğü
        electronicsDictionary.Add("ohm kanunu", "Ohm Kanunu, bir iletkenin iki ucu arasındaki potansiyel farkının (V), iletkenden geçen akım (I) ile doğru, iletkenin direnci (R) ile ters orantılı olduğunu ifade eder. Formül: V = I × R");
        electronicsDictionary.Add("kirchhoff kanunları", "Kirchhoff'un iki kanunu vardır:\n1. Akım Kanunu (KCL): Bir düğüm noktasına giren ve çıkan akımların toplamı sıfırdır.\n2. Gerilim Kanunu (KVL): Kapalı bir devredeki gerilim düşümlerinin toplamı sıfırdır.");
        electronicsDictionary.Add("direnç", "Direnç, elektrik akımına karşı gösterilen zorluktur. Birimi ohm (Ω)'dur. Renk kodlarıyla değeri belirtilir.");
        electronicsDictionary.Add("kondansatör", "Kondansatör, elektrik yükü depolayan pasif bir elektronik bileşendir. Kapasite birimi farad (F)'dır.");
        electronicsDictionary.Add("endüktans", "Endüktans, bir iletkenden geçen akımın değişimine karşı gösterilen tepkidir. Birimi henry (H)'dir.");
        electronicsDictionary.Add("transistör", "Transistör, akım kazancı sağlayan yarı iletken devre elemanıdır. BJT ve FET olmak üzere iki ana türü vardır.");
        electronicsDictionary.Add("diyot", "Diyot, akımı tek yönde geçiren yarı iletken devre elemanıdır. Doğrultucu devrelerde kullanılır.");
        electronicsDictionary.Add("rezonans", "Rezonans, bir sistemin doğal frekansıyla aynı frekansta uyarılması sonucu genliğin maksimum olduğu durumdur.");
        electronicsDictionary.Add("transformatör", "Transformatör, elektrik enerjisinin gerilim ve akım değerlerini manyetik indüksiyon yoluyla değiştiren statik bir elektrik makinesidir.");

        // Direnç Renk Kodu Tablosu
        colorCodeTable.Add("siyah", "0");
        colorCodeTable.Add("kahverengi", "1");
        colorCodeTable.Add("kırmızı", "2");
        colorCodeTable.Add("turuncu", "3");
        colorCodeTable.Add("sarı", "4");
        colorCodeTable.Add("yeşil", "5");
        colorCodeTable.Add("mavi", "6");
        colorCodeTable.Add("mor", "7");
        colorCodeTable.Add("gri", "8");
        colorCodeTable.Add("beyaz", "9");
        colorCodeTable.Add("altın", "±5%");
        colorCodeTable.Add("gümüş", "±10%");
    }

    private static Task ErrorHandler(ITelegramBotClient bot, Exception exception, CancellationToken token)
    {
        Console.WriteLine($"Hata oluştu: {exception.Message}");
        return Task.CompletedTask;
    }

    private static async Task UpdateHandler(ITelegramBotClient bot, Update update, CancellationToken token)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message != null)
            {
                await HandleMessage(update.Message);
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                await HandleCallbackQuery(update.CallbackQuery);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update işleme hatası: {ex.Message}");
        }
    }

    private static async Task HandleMessage(Message message)
    {
        if (message.Text == null)
            return;

        long chatId = message.Chat.Id;
        string messageText = message.Text.ToLower();

        if (!userStates.ContainsKey(chatId))
        {
            userStates[chatId] = new UserState { CurrentMenu = "main", PreviousMenu = "" };
        }

        var userState = userStates[chatId];

        if (messageText.StartsWith("/start"))
        {
            await ShowMainMenu(chatId);
        }
        else if (userState.CurrentCalculation != null)
        {
            await HandleCalculationInput(chatId, messageText, userState);
        }
        else if (electronicsDictionary.ContainsKey(messageText))
        {
            await SendTermInfo(chatId, messageText);
        }
        else if (IsColorCodeQuery(messageText))
        {
            await HandleColorCodeQuery(chatId, messageText);
        }
        else
        {
            // Terim sözlüğünde benzerlik kontrolü
            var similarTerm = FindSimilarTerm(messageText);
            if (similarTerm != null)
            {
                await SendTermInfo(chatId, similarTerm);
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Komut anlaşılamadı. Lütfen menüyü kullanın veya bir elektronik terimi yazın.",
                    replyMarkup: CreateMainMenuKeyboard()
                );
            }
        }
    }

    private static bool IsColorCodeQuery(string text)
    {
        var colors = text.Split(new[] { ' ', ',', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return colors.All(c => colorCodeTable.ContainsKey(c));
    }

    private static async Task HandleColorCodeQuery(long chatId, string colorQuery)
    {
        var colors = colorQuery.Split(new[] { ' ', ',', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (colors.Length < 3 || colors.Length > 4)
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Geçersiz renk kodu. 3 veya 4 renk girmelisiniz. Örnek: kırmızı mor sarı altın",
                replyMarkup: CreateMainMenuKeyboard()
            );
            return;
        }

        try
        {
            string digit1 = colorCodeTable[colors[0]];
            string digit2 = colorCodeTable[colors[1]];
            string multiplier = colorCodeTable[colors[2]];
            string tolerance = colors.Length == 4 ? colorCodeTable[colors[3]] : "±20%";

            double value = double.Parse(digit1 + digit2) * Math.Pow(10, int.Parse(multiplier));

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"🎨 *Direnç Renk Kodu Sonucu*\n\n" +
                      $"Renkler: {string.Join("-", colors)}\n" +
                      $"Değer: {value} Ω\n" +
                      $"Tolerans: {tolerance}\n\n" +
                      $"1. Bant: {digit1} ({colors[0]})\n" +
                      $"2. Bant: {digit2} ({colors[1]})\n" +
                      $"Çarpan: 10^{multiplier} ({colors[2]})\n" +
                      (colors.Length == 4 ? $"Tolerans: {tolerance} ({colors[3]})" : ""),
                parseMode: ParseMode.Markdown,
                replyMarkup: CreateMainMenuKeyboard()
            );
        }
        catch
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Renk kodunu işlerken hata oluştu. Lütfen geçerli renkler girin.",
                replyMarkup: CreateMainMenuKeyboard()
            );
        }
    }

    private static string FindSimilarTerm(string input)
    {
        input = input.ToLower();
        foreach (var term in electronicsDictionary.Keys)
        {
            if (term.Contains(input) || input.Contains(term))
            {
                return term;
            }
        }
        return null;
    }

    private static async Task SendTermInfo(long chatId, string term)
    {
        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: $"📖 *{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(term)}*\n\n{electronicsDictionary[term]}",
            parseMode: ParseMode.Markdown,
            replyMarkup: CreateMainMenuKeyboard()
        );
    }

    private static async Task HandleCalculationInput(long chatId, string input, UserState userState)
    {
        try
        {
            if (input.ToLower() == "iptal")
            {
                userState.CurrentCalculation = null;
                userState.CalculationValues.Clear();
                userState.SeriesResistors.Clear();
                userState.ParallelResistors.Clear();
                await ShowMainMenu(chatId);
                return;
            }

            if (userState.CurrentCalculation == "series_resistor" || userState.CurrentCalculation == "parallel_resistor")
            {
                if (input.ToLower() == "hesapla")
                {
                    await CalculateResistorTotal(chatId, userState);
                    return;
                }

                if (double.TryParse(input, out double value))
                {
                    if (userState.CurrentCalculation == "series_resistor")
                    {
                        userState.SeriesResistors.Add(value);
                    }
                    else
                    {
                        userState.ParallelResistors.Add(value);
                    }

                    await AskForNextResistorValue(chatId, userState);
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Geçersiz değer. Lütfen sayısal bir değer girin veya 'iptal' yazın."
                    );
                }
            }
            else
            {
                if (!userState.CalculationValues.ContainsKey("value1"))
                {
                    userState.CalculationValues["value1"] = input;
                    await AskForNextCalculationValue(chatId, userState);
                }
                else if (!userState.CalculationValues.ContainsKey("value2"))
                {
                    userState.CalculationValues["value2"] = input;
                    await PerformCalculation(chatId, userState);
                }
                else if (!userState.CalculationValues.ContainsKey("value3"))
                {
                    userState.CalculationValues["value3"] = input;
                    await PerformCalculation(chatId, userState);
                }
            }
        }
        catch (Exception ex)
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Hesaplama sırasında hata oluştu: {ex.Message}\nLütfen geçerli değerler girin.",
                replyMarkup: CreateMainMenuKeyboard()
            );
            userState.CurrentCalculation = null;
            userState.CalculationValues.Clear();
        }
    }

    private static async Task AskForNextResistorValue(long chatId, UserState userState)
    {
        string message = userState.CurrentCalculation == "series_resistor" ?
            "Seri bağlı bir direnç değeri daha girin (Ω cinsinden) veya 'hesapla' yazın:" :
            "Paralel bağlı bir direnç değeri daha girin (Ω cinsinden) veya 'hesapla' yazın:";

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message
        );
    }

    private static async Task CalculateResistorTotal(long chatId, UserState userState)
    {
        string result = "";
        if (userState.CurrentCalculation == "series_resistor" && userState.SeriesResistors.Count > 0)
        {
            double total = userState.SeriesResistors.Sum();
            result = $"🔢 *Seri Direnç Hesaplama*\n\nToplam Direnç: {total} Ω\n\n" +
                     $"Dirençler: {string.Join("Ω + ", userState.SeriesResistors)}Ω";
        }
        else if (userState.CurrentCalculation == "parallel_resistor" && userState.ParallelResistors.Count > 0)
        {
            double reciprocalSum = userState.ParallelResistors.Sum(r => 1 / r);
            double total = 1 / reciprocalSum;
            result = $"🔢 *Paralel Direnç Hesaplama*\n\nToplam Direnç: {total.ToString("0.##")} Ω\n\n" +
                     $"Dirençler: 1/({string.Join(" + ", userState.ParallelResistors.Select(r => $"1/{r}Ω"))})";
        }
        else
        {
            result = "Hesaplama yapmak için en az bir direnç değeri girmelisiniz.";
        }

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: result,
            parseMode: ParseMode.Markdown,
            replyMarkup: CreateMainMenuKeyboard()
        );

        userState.CurrentCalculation = null;
        userState.SeriesResistors.Clear();
        userState.ParallelResistors.Clear();
    }

    private static async Task AskForNextCalculationValue(long chatId, UserState userState)
    {
        string message = "";
        string currentCalc = userState.CurrentCalculation;

        if (currentCalc == "ohm_law")
        {
            if (!userState.CalculationValues.ContainsKey("value1"))
                message = "Voltaj (V) değerini girin veya 'iptal' yazın:";
            else
                message = "Direnç (Ω) değerini girin veya 'iptal' yazın:";
        }
        else if (currentCalc == "power_calc")
        {
            if (!userState.CalculationValues.ContainsKey("value1"))
                message = "Voltaj (V) değerini girin veya 'iptal' yazın:";
            else
                message = "Akım (A) değerini girin veya 'iptal' yazın:";
        }
        else if (currentCalc == "resistor_code")
        {
            message = "Direnç değerini (ohm cinsinden) girin veya 'iptal' yazın:";
        }
        else if (currentCalc == "resonance_freq")
        {
            if (!userState.CalculationValues.ContainsKey("value1"))
                message = "Endüktans (H) değerini girin veya 'iptal' yazın:";
            else
                message = "Kapasite (F) değerini girin veya 'iptal' yazın:";
        }
        else if (currentCalc == "transformer_calc")
        {
            if (!userState.CalculationValues.ContainsKey("value1"))
                message = "Primer gerilim (V) değerini girin veya 'iptal' yazın:";
            else if (!userState.CalculationValues.ContainsKey("value2"))
                message = "Sekonder gerilim (V) değerini girin veya 'iptal' yazın:";
            else
                message = "Primer akım (A) değerini girin veya 'iptal' yazın:";
        }

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message
        );
    }

    private static async Task PerformCalculation(long chatId, UserState userState)
    {
        string result = "";
        string currentCalc = userState.CurrentCalculation;

        try
        {
            if (currentCalc == "ohm_law")
            {
                double v = double.Parse(userState.CalculationValues["value1"]);
                double r = double.Parse(userState.CalculationValues["value2"]);
                double i = v / r;
                result = $"🔌 *Ohm Kanunu Sonucu*\n\nVoltaj (V): {v} V\nDirenç (R): {r} Ω\nAkım (I): {i.ToString("0.000")} A";
            }
            else if (currentCalc == "power_calc")
            {
                double v = double.Parse(userState.CalculationValues["value1"]);
                double i = double.Parse(userState.CalculationValues["value2"]);
                double p = v * i;
                result = $"⚡ *Güç Hesaplama Sonucu*\n\nVoltaj (V): {v} V\nAkım (I): {i} A\nGüç (P): {p.ToString("0.000")} W";
            }
            else if (currentCalc == "resistor_code")
            {
                double value = double.Parse(userState.CalculationValues["value1"]);
                result = CalculateResistorColorCode(value);
            }
            else if (currentCalc == "resonance_freq")
            {
                double l = double.Parse(userState.CalculationValues["value1"]);
                double c = double.Parse(userState.CalculationValues["value2"]);
                double fr = 1 / (2 * Math.PI * Math.Sqrt(l * c));
                result = $"🌀 *Rezonans Frekansı*\n\nEndüktans (L): {l} H\nKapasite (C): {c} F\n" +
                         $"Rezonans Frekansı: {fr.ToString("0.##")} Hz\n\n" +
                         $"Formül: fₙ = 1 / (2π√(LC))";
            }
            else if (currentCalc == "transformer_calc")
            {
                double v1 = double.Parse(userState.CalculationValues["value1"]);
                double v2 = double.Parse(userState.CalculationValues["value2"]);
                double i1 = double.Parse(userState.CalculationValues["value3"]);
                
                double ratio = v1 / v2;
                double i2 = i1 * ratio;
                
                result = $"🔌 *Transformatör Hesaplama*\n\n" +
                         $"Primer Gerilim (V₁): {v1} V\nSekonder Gerilim (V₂): {v2} V\n" +
                         $"Primer Akım (I₁): {i1} A\nSekonder Akım (I₂): {i2.ToString("0.##")} A\n" +
                         $"Dönüştürme Oranı: {ratio.ToString("0.##")}\n\n" +
                         $"Formüller:\nV₁/V₂ = N₁/N₂ = I₂/I₁";
            }

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: result,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("◀️ Geri", "back"),
                    InlineKeyboardButton.WithCallbackData("🏠 Ana Menü", "main_menu")
                })
            );
        }
        catch (Exception ex)
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Hesaplama hatası: {ex.Message}",
                replyMarkup: CreateMainMenuKeyboard()
            );
        }
        finally
        {
            userState.CurrentCalculation = null;
            userState.CalculationValues.Clear();
        }
    }

    private static string CalculateResistorColorCode(double value)
    {
        if (value <= 0) return "Geçersiz direnç değeri";

        int exponent = 0;
        while (value >= 100 && exponent < 8)
        {
            value /= 10;
            exponent++;
        }

        while (value < 10 && exponent > -2)
        {
            value *= 10;
            exponent--;
        }

        int val = (int)Math.Round(value);
        if (val >= 100) val = 99;
        if (val < 10) val = 10;

        int digit1 = val / 10;
        int digit2 = val % 10;

        Dictionary<int, string> colorCodes = new Dictionary<int, string>()
        {
            {0, "Siyah"}, {1, "Kahverengi"}, {2, "Kırmızı"},
            {3, "Turuncu"}, {4, "Sarı"}, {5, "Yeşil"},
            {6, "Mavi"}, {7, "Mor"}, {8, "Gri"}, {9, "Beyaz"}
        };

        Dictionary<int, string> multiplierCodes = new Dictionary<int, string>()
        {
            {-2, "Gümüş"}, {-1, "Altın"}, {0, "Siyah"},
            {1, "Kahverengi"}, {2, "Kırmızı"}, {3, "Turuncu"},
            {4, "Sarı"}, {5, "Yeşil"}, {6, "Mavi"}, {7, "Mor"}, {8, "Gri"}
        };

        Dictionary<int, string> toleranceCodes = new Dictionary<int, string>()
        {
            {1, "Kahverengi ±1%"}, {2, "Kırmızı ±2%"}, {0, "Yok ±20%"},
            {5, "Altın ±5%"}, {10, "Gümüş ±10%"}
        };

        string color1 = colorCodes[digit1];
        string color2 = colorCodes[digit2];
        string multiplier = multiplierCodes[exponent];
        string tolerance = toleranceCodes[exponent == -1 ? 5 : exponent == -2 ? 10 : 0];

        string colorTable = "🎨 *Direnç Renk Kodu Tablosu*\n\n" +
                           "Renk | Değer | Çarpan | Tolerans\n" +
                           "---- | ----- | ------ | --------\n" +
                           "Siyah | 0 | 10⁰ | -\n" +
                           "Kahverengi | 1 | 10¹ | ±1%\n" +
                           "Kırmızı | 2 | 10² | ±2%\n" +
                           "Turuncu | 3 | 10³ | -\n" +
                           "Sarı | 4 | 10⁴ | -\n" +
                           "Yeşil | 5 | 10⁵ | ±0.5%\n" +
                           "Mavi | 6 | 10⁶ | ±0.25%\n" +
                           "Mor | 7 | 10⁷ | ±0.1%\n" +
                           "Gri | 8 | 10⁸ | ±0.05%\n" +
                           "Beyaz | 9 | 10⁹ | -\n" +
                           "Altın | - | 10⁻¹ | ±5%\n" +
                           "Gümüş | - | 10⁻² | ±10%";

        return $"🎨 *Direnç Renk Kodu*\n\nDeğer: {value * Math.Pow(10, exponent)} Ω\n" +
               $"Renk Kodu: {color1} - {color2} - {multiplier}\n" +
               $"Tolerans: {tolerance}\n\n" +
               $"1. Bant: {digit1} ({color1})\n" +
               $"2. Bant: {digit2} ({color2})\n" +
               $"Çarpan: 10^{exponent} ({multiplier})\n\n" +
               colorTable;
    }

    private static async Task HandleCallbackQuery(CallbackQuery callbackQuery)
    {
        long chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data;

        if (!userStates.ContainsKey(chatId))
        {
            userStates[chatId] = new UserState { CurrentMenu = "main", PreviousMenu = "" };
        }

        var userState = userStates[chatId];

        switch (data)
        {
            case "main_menu":
                userState.CurrentMenu = "main";
                userState.CurrentCalculation = null;
                userState.CalculationValues.Clear();
                await ShowMainMenu(chatId);
                break;
            case "formulas":
                userState.CurrentMenu = "formulas";
                await ShowFormulasMenu(chatId);
                break;
            case "components":
                userState.CurrentMenu = "components";
                await ShowComponentsMenu(chatId);
                break;
            case "calculators":
                userState.CurrentMenu = "calculators";
                await ShowCalculatorsMenu(chatId);
                break;
            case "circuit_analysis":
                userState.CurrentMenu = "circuit_analysis";
                await ShowCircuitAnalysisMenu(chatId);
                break;
            case "ohm_law":
                userState.CurrentCalculation = "ohm_law";
                userState.CalculationValues.Clear();
                await AskForNextCalculationValue(chatId, userState);
                break;
            case "power_calc":
                userState.CurrentCalculation = "power_calc";
                userState.CalculationValues.Clear();
                await AskForNextCalculationValue(chatId, userState);
                break;
            case "resistor_code":
                userState.CurrentCalculation = "resistor_code";
                userState.CalculationValues.Clear();
                await AskForNextCalculationValue(chatId, userState);
                break;
            case "series_resistor":
                userState.CurrentCalculation = "series_resistor";
                userState.SeriesResistors.Clear();
                await AskForNextResistorValue(chatId, userState);
                break;
            case "parallel_resistor":
                userState.CurrentCalculation = "parallel_resistor";
                userState.ParallelResistors.Clear();
                await AskForNextResistorValue(chatId, userState);
                break;
            case "resonance_freq":
                userState.CurrentCalculation = "resonance_freq";
                userState.CalculationValues.Clear();
                await AskForNextCalculationValue(chatId, userState);
                break;
            case "transformer_calc":
                userState.CurrentCalculation = "transformer_calc";
                userState.CalculationValues.Clear();
                await AskForNextCalculationValue(chatId, userState);
                break;
            case "kirchhoff_law":
                await SendFormulaInfo(chatId, "Kirchhoff Kanunları", 
                    "1. Kirchhoff Akım Kanunu (KCL): Bir düğüm noktasına giren akımların toplamı, çıkan akımların toplamına eşittir.\n" +
                    "∑I_giren = ∑I_cikan\n\n" +
                    "2. Kirchhoff Gerilim Kanunu (KVL): Kapalı bir devre çevresindeki gerilim düşümlerinin toplamı sıfırdır.\n" +
                    "∑V = 0\n\n" +
                    "Hesaplama araçları menüsünden devre analizi yapabilirsiniz.");
                break;
            case "series_circuit":
                await SendFormulaInfo(chatId, "Seri Devre Hesaplamaları", 
                    "Seri devrede:\n" +
                    "- Toplam direnç: R_toplam = R₁ + R₂ + ... + Rₙ\n" +
                    "- Tüm dirençlerden aynı akım geçer\n" +
                    "- Gerilimler dirençler arasında paylaşılır\n\n" +
                    "Hesaplama yapmak için 'Seri Direnç Hesaplama' aracını kullanabilirsiniz.");
                break;
            case "parallel_circuit":
                await SendFormulaInfo(chatId, "Paralel Devre Hesaplamaları", 
                    "Paralel devrede:\n" +
                    "- Toplam direnç: 1/R_toplam = 1/R₁ + 1/R₂ + ... + 1/Rₙ\n" +
                    "- Tüm dirençlerde aynı gerilim vardır\n" +
                    "- Akım dirençler arasında paylaşılır\n\n" +
                    "Hesaplama yapmak için 'Paralel Direnç Hesaplama' aracını kullanabilirsiniz.");
                break;
            case "ac_circuit":
                await SendFormulaInfo(chatId, "AC Devre Analizi", 
                    "Alternatif Akım (AC) Devrelerinde:\n" +
                    "- Empedans (Z): √(R² + (Xₗ - X꜀)²)\n" +
                    "- Reaktans (Xₗ): 2πfL\n" +
                    "- Kapasitif Reaktans (X꜀): 1/(2πfC)\n" +
                    "- Faz Açısı (θ): tan⁻¹((Xₗ - X꜀)/R)\n\n" +
                    "Hesaplama araçları menüsünden AC devre analizi yapabilirsiniz.");
                break;
            case "back":
                if (userState.PreviousMenu != "")
                {
                    string temp = userState.CurrentMenu;
                    userState.CurrentMenu = userState.PreviousMenu;
                    userState.PreviousMenu = temp;
                    
                    switch (userState.CurrentMenu)
                    {
                        case "main":
                            await ShowMainMenu(chatId);
                            break;
                        case "formulas":
                            await ShowFormulasMenu(chatId);
                            break;
                        case "components":
                            await ShowComponentsMenu(chatId);
                            break;
                        case "calculators":
                            await ShowCalculatorsMenu(chatId);
                            break;
                        case "circuit_analysis":
                            await ShowCircuitAnalysisMenu(chatId);
                            break;
                    }
                }
                else
                {
                    await ShowMainMenu(chatId);
                }
                break;
            default:
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Seçenek bulunamadı. Ana menüye yönlendiriliyorsunuz...",
                    replyMarkup: CreateMainMenuKeyboard()
                );
                break;
        }

        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
    }

    private static async Task ShowMainMenu(long chatId)
    {
        userStates[chatId].CurrentMenu = "main";
        userStates[chatId].PreviousMenu = "";

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "🏠 *Ana Menü* - Hoş geldiniz! Aşağıdaki seçeneklerden birini seçin:",
            parseMode: ParseMode.Markdown,
            replyMarkup: CreateMainMenuKeyboard()
        );
    }

    private static async Task ShowFormulasMenu(long chatId)
    {
        userStates[chatId].PreviousMenu = userStates[chatId].CurrentMenu;
        userStates[chatId].CurrentMenu = "formulas";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Ohm Kanunu", "ohm_law"),
                InlineKeyboardButton.WithCallbackData("Kirchhoff Kanunu", "kirchhoff_law")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Güç Hesapları", "power_calc"),
                InlineKeyboardButton.WithCallbackData("Seri Devre", "series_circuit")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Paralel Devre", "parallel_circuit"),
                InlineKeyboardButton.WithCallbackData("AC Devre", "ac_circuit")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("◀️ Geri", "back")
            }
        });

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "📊 *Formüller ve Hesaplamalar* - Aşağıdaki formüllerden birini seçin:",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard
        );
    }

    private static async Task ShowComponentsMenu(long chatId)
    {
        userStates[chatId].PreviousMenu = userStates[chatId].CurrentMenu;
        userStates[chatId].CurrentMenu = "components";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Direnç Renk Kodları", "resistor_code"),
                InlineKeyboardButton.WithCallbackData("Kondansatör Türleri", "capacitor_types")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Diyotlar", "diodes_info"),
                InlineKeyboardButton.WithCallbackData("Transistörler", "transistors_info")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("◀️ Geri", "back")
            }
        });

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "🔌 *Komponent Bilgileri* - Aşağıdaki seçeneklerden birini seçin:",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard
        );
    }

    private static async Task ShowCalculatorsMenu(long chatId)
    {
        userStates[chatId].PreviousMenu = userStates[chatId].CurrentMenu;
        userStates[chatId].CurrentMenu = "calculators";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Ohm Kanunu", "ohm_law"),
                InlineKeyboardButton.WithCallbackData("Güç Hesaplama", "power_calc")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Direnç Renk Kodu", "resistor_code"),
                InlineKeyboardButton.WithCallbackData("Seri Direnç", "series_resistor")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Paralel Direnç", "parallel_resistor"),
                InlineKeyboardButton.WithCallbackData("Rezonans Frekansı", "resonance_freq")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Transformatör", "transformer_calc")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("◀️ Geri", "back")
            }
        });

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "🧮 *Hesaplama Araçları* - Aşağıdaki hesaplamalardan birini seçin:",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard
        );
    }

    private static async Task ShowCircuitAnalysisMenu(long chatId)
    {
        userStates[chatId].PreviousMenu = userStates[chatId].CurrentMenu;
        userStates[chatId].CurrentMenu = "circuit_analysis";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Seri Devre", "series_circuit"),
                InlineKeyboardButton.WithCallbackData("Paralel Devre", "parallel_circuit")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Karışık Devre", "mixed_circuit"),
                InlineKeyboardButton.WithCallbackData("Thevenin Teoremi", "thevenin_theorem")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("AC Devre Analizi", "ac_circuit"),
                InlineKeyboardButton.WithCallbackData("Rezonans Devresi", "resonance_circuit")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("◀️ Geri", "back")
            }
        });

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "🔍 *Devre Analizi Araçları* - Aşağıdaki analizlerden birini seçin:",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard
        );
    }

    private static async Task SendFormulaInfo(long chatId, string title, string content)
    {
        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: $"📐 *{title}*\n\n{content}",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                InlineKeyboardButton.WithCallbackData("◀️ Geri", "back"),
                InlineKeyboardButton.WithCallbackData("🏠 Ana Menü", "main_menu")
            })
        );
    }

    private static InlineKeyboardMarkup CreateMainMenuKeyboard()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Formüller", "formulas"),
                InlineKeyboardButton.WithCallbackData("🔌 Komponentler", "components")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🧮 Hesaplamalar", "calculators"),
                InlineKeyboardButton.WithCallbackData("🔍 Devre Analizi", "circuit_analysis")
            }
        });
    }
}