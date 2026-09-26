using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GooseDeluxe;
using GooseShared;
using SamEngine;

namespace Tests
{
    /// <summary>The talking goose: phrases file, syllables, both voices, the bubble, the speaker and the task.</summary>
    internal static class SpeechTests
    {
        private static void Check(string what, bool ok, string detail = null) { Program.Check(what, ok, detail); }

        public static void Phrases()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gd-phrases-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string builtIn = PhraseBook.DefaultText();
                List<string> all = PhraseBook.Parse(builtIn);
                Check("в мод вшиты 30 фраз пользователя", all.Count == 30, all.Count.ToString());
                Check("первая — «Инженер ПТО! Не ПТО, а хуй в пальто!»", all.Count > 0 && all[0] == "Инженер ПТО! Не ПТО, а хуй в пальто!");
                Check("комментарии (#) гусь не говорит", all.All(p => !p.StartsWith("#")));

                PhraseBook book = new PhraseBook(dir, new Random(5));
                string file = Path.Combine(dir, PhraseBook.FileName);
                byte[] head = File.ReadAllBytes(file).Take(3).ToArray();
                Check("Фразы.txt создаётся рядом с модом (UTF-8 с BOM, для Блокнота)", File.Exists(file) && head.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
                Check("в файле те же 30 фраз", book.Count == 30, book.Count.ToString());

                HashSet<string> round = new HashSet<string>();
                string prev = null;
                bool noDouble = true, peekOk = true;
                for (int i = 0; i < 90; i++)
                {
                    string peek = book.Peek();
                    string p = book.Next();
                    if (peek != p) peekOk = false;
                    if (p == prev) noDouble = false;
                    prev = p;
                    if (i < 30) round.Add(p);
                }
                Check("по кругу: за 30 раз — все 30 разных", round.Count == 30, round.Count.ToString());
                Check("одна и та же два раза подряд — никогда (и на стыке кругов)", noDouble);
                Check("«следующая» заранее совпадает с той, что будет сказана", peekOk);

                // the user edits the file: own phrases, list bullets, «quotes», comments, no BOM
                File.WriteAllText(file, "# мои\n* «Где исполнительная?!»\n\n- \"Сдал КС-2 — гуляй\"\nПросто фраза\n", new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
                List<string> mine = new List<string>();
                for (int i = 0; i < 3; i++) mine.Add(book.Next());
                mine.Sort(StringComparer.Ordinal);
                Check("свои фразы подхватываются сами, без перезапуска", book.Count == 3 && string.Join("|", mine) == "Где исполнительная?!|Просто фраза|Сдал КС-2 — гуляй",
                      string.Join("|", mine));

                new PhraseBook(dir); // a restart must not overwrite the user's file
                Check("свой файл при перезапуске не затирается", File.ReadAllText(file).Contains("Где исполнительная"));

                File.WriteAllText(file, "# всё стёрли\n");
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(10));
                Check("файл опустел — гусь говорит прежние, а не молчит", book.Count == 3 && book.Next() != null);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void Text()
        {
            List<SpeechText.Word> w = SpeechText.Words("Инженер ПТО! Не ПТО, а хуй в пальто!");
            Check("слоги: инженер (3) ПТО (пэ-тэ-о, 3)", w.Count == 8 && w[0].Syllables == 3 && w[1].Syllables == 3,
                  string.Join(" ", w.Select(x => x.Syllables)));
            Check("«!» — конец фразы, пауза после «ПТО!»", w[1].Mark == '!' && w[1].PauseAfter >= 0.3);
            Check("«в» без гласной — без своего слога", w[6].Syllables == 0 && w[7].Syllables == 2);
            Check("«дураааак» — два слога, растянутая «а» один раз", SpeechText.Words("дураааак")[0].Syllables == 2);
            Check("«П-Т-О» — три буквы по слогу", SpeechText.Words("П-Т-О")[0].Syllables == 1 && SpeechText.Words("П-Т-О").Count == 3);
            Check("числа читаются: 3121 — «три-о-дин-два-о-дин»", SpeechText.Words("3121")[0].Syllables == 6, SpeechText.Words("3121")[0].Syllables.ToString());
            Check("«?!» — вопрос", SpeechText.Words("Где акты?!")[1].Mark == '?');
            Check("многоточие — длинная пауза", SpeechText.Words("ПТО… Купи")[0].PauseAfter >= 0.45);

            bool odd = true;
            foreach (string t in new[] { "КС-٣ сдана", "объект ３１２１", "²³ ½", "𝟙𝟚𝟛", "", "!!!", "-", "ПТО-", "a1b2", new string('ж', 500) })
                try { SpeechText.Words(t); SpeechText.ForSynthesizer(t); Utterance.Silent(t, "x"); double[] tt; GooseVoice.Render(t, out tt); }
                catch (Exception ex) { odd = false; Console.WriteLine("      " + t + ": " + ex.GetType().Name); }
            Check("странные цифры, пустые и очень длинные фразы не ломают гуся", odd);

            string tts = SpeechText.ForSynthesizer("ПТОшник! КС-2, ОВ и ВК, СНиПы, ГОСТам. Ни-ху-я! Ой, дураааак… П-Т-О, гусь-технадзор");
            Check("для голоса Windows: ПТОшник → пэтэошник", tts.Contains("пэтэошник"), tts);
            Check("КС-2 → ка-эс-2, ОВ → о-вэ, ВК → вэ-ка", tts.Contains("ка-эс-2") && tts.Contains("о-вэ") && tts.Contains("вэ-ка"), tts);
            Check("СНиПы и ГОСТам — как слова", tts.Contains("СНиПы") && tts.Contains("ГОСТам"), tts);
            Check("Ни-ху-я → «Ни ху я», гусь-технадзор — через дефис", tts.Contains("Ни ху я") && tts.Contains("гусь-технадзор"), tts);
            Check("дураааак → дураак, П-Т-О → пэ тэ о", tts.Contains("дураак") && !tts.Contains("дурааак") && tts.ToLowerInvariant().Contains("пэ тэ о"), tts);
        }

        public static void Commands()
        {
            Check("«скажи Где акты?!» — гусь говорит это", FriendProtocol.ParseSay("скажи Где акты?!") == "Где акты?!");
            Check("«Скажи: привет» — тоже", FriendProtocol.ParseSay("Скажи: привет") == "привет");
            Check("«Скажи, как дела?» — это записка, а не команда", FriendProtocol.ParseSay("Скажи, как дела?") == null);
            Check("«скажи» и «фраза» — случайная фраза", FriendProtocol.ParseCommand("скажи") == GooseCommand.Phrase && FriendProtocol.ParseCommand("Фраза!") == GooseCommand.Phrase);
            Incoming phone = FriendProtocol.Parse(new NtfyMessage { id = "1", @event = "message", message = "скажи Инженер ПТО, где твоя каска?" }, "https://ntfy.sh");
            Check("с телефона «скажи …» — команда с текстом", phone != null && phone.Kind == IncomingKind.Command && phone.Command == GooseCommand.Say && phone.Text == "Инженер ПТО, где твоя каска?");
            Incoming note = FriendProtocol.Parse(new NtfyMessage { id = "2", @event = "message", message = "скажи Вася, я опоздаю", title = "Петя", tags = new[] { "gd", "from-gusaaaabbbbcccc" } }, "https://ntfy.sh");
            Check("от гуся друга «скажи …» — записка, гусь её приносит", note != null && note.Kind == IncomingKind.Note);
            Incoming prank = FriendProtocol.Parse(new NtfyMessage { id = "3", @event = "message", message = FriendProtocol.CommandWord(GooseCommand.Phrase), tags = new[] { "gd", "from-gusaaaabbbbcccc" } }, "https://ntfy.sh");
            Check("«Сказать другу фразу» доходит как команда", prank != null && prank.Kind == IncomingKind.Command && prank.Command == GooseCommand.Phrase);
            List<string> dup = PhraseBook.Parse("Раз\nраз\nДва\n");
            Check("одинаковые строки в файле — одна фраза", dup.Count == 2);
        }

        public static void GooseVoiceTest()
        {
            List<string> all = PhraseBook.Parse(PhraseBook.DefaultText());
            double minD = 99, maxD = 0;
            bool finite = true, monotonic = true, full = true, loud = true, mouth = true;
            foreach (string p in all)
            {
                double[] times;
                float[] s = GooseVoice.Render(p, out times);
                double d = s.Length / (double)GooseVoice.Rate;
                minD = Math.Min(minD, d);
                maxD = Math.Max(maxD, d);
                if (s.Any(x => float.IsNaN(x) || float.IsInfinity(x))) finite = false;
                float peak = s.Max(x => Math.Abs(x));
                if (peak < 0.8f || peak > 0.86f) loud = false;
                Utterance u = Utterance.FromSamples(p, s, GooseVoice.Rate, null, GooseVoice.Name, times);
                int last = 0;
                for (double t = 0; t < u.Duration; t += 0.05)
                {
                    int r = u.RevealAt(t);
                    if (r < last) monotonic = false;
                    last = r;
                }
                if (u.RevealAt(u.Duration) != p.Length) full = false;
                int open = 0;
                for (double t = 0; t < u.Duration; t += 0.02) if (u.MouthAt(t) > 0.3f) open++;
                if (open < 5) mouth = false;
            }
            Check("гусиный голос: все 30 фраз от 1 до 12 с", minD > 1 && maxD < 12, minD.ToString("0.0") + "–" + maxD.ToString("0.0") + " с");
            Check("гусиный голос: без NaN и щелчков за пределами", finite);
            Check("гусиный голос: громкость ровная (пик 0,85)", loud);
            Check("текст в облачке появляется по порядку и к концу — весь", monotonic && full);
            Check("клюв открывается на слогах", mouth);

            double[] tt;
            float[] honk = GooseVoice.Render("Га-га-га!", out tt);
            double f0 = DominantPitch(honk, GooseVoice.Rate, 300, 1000);
            Check("гусиный голос звучит как гудок гуся (основной тон 560–800 Гц)", f0 > 560 && f0 < 800, f0.ToString("0") + " Гц");
            Check("три «га» — три слова вовремя", tt.Length == 3 && tt[0] < tt[1] && tt[1] < tt[2]);
        }

        public static void AtomicVoice()
        {
            // a stand-in for the Windows voice: a 210 Hz "woman's" voice with harmonics and vibrato, 1.5 s,
            // with silence around it
            int rate = 22050;
            float[] v = new float[(int)(2.0 * rate)];
            double ph = 0;
            for (int i = (int)(0.25 * rate); i < (int)(1.75 * rate); i++)
            {
                double t = i / (double)rate;
                ph += 2 * Math.PI * 210 * (1 + 0.02 * Math.Sin(2 * Math.PI * 5 * t)) / rate;
                double s = 0;
                for (int h = 1; h <= 12; h++) s += Math.Sin(h * ph) / h;
                v[i] = (float)(s * 0.3 * Math.Sin(Math.PI * (t - 0.25) / 1.5));
            }
            float[] g = VoiceFx.Goose(v, rate, 0.72);
            double speech = 1.5 + 0.06;
            Check("голос как в Atomic Heart: тишина по краям обрезана, темп медленнее ×1/0,72",
                  Math.Abs(g.Length / (double)rate - speech / 0.72) < 0.08, (g.Length / (double)rate).ToString("0.00") + " с");
            double f0 = DominantPitch(g, rate, 80, 400);
            Check("и ниже: 210 Гц → около 150 Гц (мужской, «гусь-мужик»)", f0 > 135 && f0 < 165, f0.ToString("0") + " Гц");
            Check("без NaN, пик 0,9", g.All(x => !float.IsNaN(x) && !float.IsInfinity(x)) && Math.Abs(g.Max(x => Math.Abs(x)) - 0.9f) < 0.01f);
            Check("начинается и кончается тихо (без щелчка)", Math.Abs(g[0]) < 0.01f && Math.Abs(g[g.Length - 1]) < 0.01f);
            Utterance u = Utterance.FromSamples("Инженер ПТО, где твоя каска?", g, rate, null, "тест");
            Check("слова ложатся на звук: первое — в начале речи, последнее — до конца",
                  u.WordTimes[0] < 0.2 && u.WordTimes[u.WordTimes.Length - 1] < u.Duration - 0.1, string.Join(" ", u.WordTimes.Select(x => x.ToString("0.00"))));
        }

        /// <summary>Strongest pitch between lo and hi Hz by autocorrelation over the loud middle.</summary>
        private static double DominantPitch(float[] s, int rate, double lo, double hi)
        {
            int mid = s.Length / 2, n = Math.Min(4096, s.Length / 2);
            int from = Math.Max(0, mid - n / 2);
            int a = (int)(rate / hi), b = (int)(rate / lo);
            double[] c = new double[b + 2];
            double best = 0;
            for (int lag = a; lag <= b + 1; lag++)
            {
                for (int i = from; i < from + n && i + lag < s.Length; i++) c[lag] += s[i] * s[i + lag];
                if (lag <= b) best = Math.Max(best, c[lag]);
            }
            // a periodic sound correlates at every multiple of its period: the first strong peak is the pitch
            for (int lag = a + 1; lag <= b; lag++)
                if (c[lag] >= 0.8 * best && c[lag] >= c[lag - 1] && c[lag] >= c[lag + 1]) return rate / (double)lag;
            return 0;
        }

        public static void Bubble()
        {
            using (Bitmap bmp = new Bitmap(1280, 720, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                string longest = PhraseBook.Parse(PhraseBook.DefaultText()).OrderByDescending(p => p.Length).First();
                SpeechBubble b = new SpeechBubble(longest);
                b.Layout(g);
                Vector2 screen = new Vector2(1280, 720);
                bool below;
                RectangleF r = b.Place(new Vector2(640, 400), new Vector2(640, 470), screen, out below);
                Check("длинная фраза переносится на строки, облачко не шире 330 px", b.Lines >= 3 && r.Width <= SpeechBubble.MaxTextWidth + 2 * SpeechBubble.Pad + 1,
                      b.Lines + " строк, " + r.Width.ToString("0") + " px");
                Check("облачко над головой", !below && r.Bottom <= 400 - SpeechBubble.TailLength + 0.5f);
                bool inside = true;
                foreach (Vector2 head in new[] { new Vector2(5, 30), new Vector2(1275, 30), new Vector2(5, 700), new Vector2(1275, 700), new Vector2(640, 5) })
                {
                    RectangleF q = b.Place(head, new Vector2(head.x, Math.Min(715, head.y + 70)), screen, out below);
                    if (q.Left < 0 || q.Top < 0 || q.Right > 1280 || q.Bottom > 720) inside = false;
                }
                Check("у края экрана облачко не вылезает за экран (у верха — под гусём)", inside);
                b.Draw(g, new Vector2(640, 400), new Vector2(640, 470), screen, longest.Length / 2, 1f);
                int ink = 0;
                for (int x = (int)r.Left; x < r.Right; x += 3)
                    for (int y = (int)r.Top; y < r.Bottom; y += 3)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (c.A > 200 && c.R < 90) ink++;
                    }
                Check("облачко рисуется: белое, с тёмным текстом", bmp.GetPixel((int)r.Left + 20, (int)r.Top + 4).A > 200 && ink > 40, "текст: " + ink);
            }
        }

        private sealed class FakePlayer : Speaker.IPlayer
        {
            public readonly List<string> Played = new List<string>();
            public int Stops;
            public void Play(string wavPath, int volume) { Played.Add(Path.GetFileName(wavPath) + "@" + volume); }
            public void Stop() { Stops++; }
        }

        public static void SpeakerTest()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gd-voice-" + Guid.NewGuid().ToString("N"));
            try
            {
                FakePlayer player = new FakePlayer();
                int built = 0;
                Speaker sp = new Speaker(player, (text, voice) =>
                {
                    Interlocked.Increment(ref built);
                    return voice == "Off" ? Utterance.Silent(text, "без голоса") : GooseVoice.Say(text, Path.Combine(dir, Utterance.FileNameFor(voice, text)));
                });
                double now = 100;
                sp.Say("Инженер ПТО, где твоя каска?", "Goose", true, 850, false, now);
                Check("фраза готовится в фоне — гусь сразу «занят»", sp.Busy && sp.Talking(now));
                for (int i = 0; i < 200 && player.Played.Count == 0; i++) { Thread.Sleep(20); sp.Update(now); }
                Check("готова — сразу играет через MCI с громкостью 850", player.Played.Count == 1 && player.Played[0].EndsWith("@850"), string.Join(",", player.Played));
                Check("WAV лежит на диске", Directory.Exists(dir) && Directory.GetFiles(dir, "*.wav").Length == 1);
                double start = now;
                bool mouthMoved = false;
                for (double t = 0; t < 2; t += 0.02) if (sp.Mouth(start + t) > 0.3f) mouthMoved = true;
                Check("клюв двигается, пока гусь говорит", mouthMoved);
                sp.Update(start + 30);
                Check("сказал, облачко повисело и ушло", !sp.Busy);

                sp.Say("Остановите стройку, я сойду!", "Goose", true, 850, true, 200);
                for (int i = 0; i < 200; i++) { Thread.Sleep(10); sp.Update(200); }
                Check("пока гусь идёт к курсору — молчит", player.Played.Count == 1 && sp.Held);
                sp.Release();
                sp.Update(200.1);
                Check("дошёл — говорит", player.Played.Count == 2);

                sp.Say("Ты заходи, если что… как исполнительную переделаешь!", "Goose", false, 850, false, 300);
                for (int i = 0; i < 200 && sp.CurrentText != null && !sp.Talking(300) ; i++) { Thread.Sleep(10); sp.Update(300); }
                for (int i = 0; i < 200 && player.Played.Count == 2 && sp.Mouth(300.5) == 0f; i++) { Thread.Sleep(10); sp.Update(300); }
                Check("без звука (гусь без звука) — только облачко и клюв", player.Played.Count == 2 && sp.Mouth(300.5) >= 0f && sp.Busy);
                sp.Stop();
                Check("пауза: всё стихает сразу", !sp.Busy && player.Stops > 0);

                sp.Prepare("Инженер ПТО, где твоя каска?", "Goose");
                Check("готовая фраза второй раз не собирается", built == 3, built.ToString());

                Speaker broken = new Speaker(player, (t, v) => { throw new InvalidOperationException("нет голоса"); });
                broken.Say("Где общий журнал работ?!", "Atomic", true, 850, false, 400);
                for (int i = 0; i < 200 && broken.Said == 0; i++) { Thread.Sleep(10); broken.Update(400); }
                Check("голос сломался — фраза всё равно видна (облачко), ошибка в «Проверке»", broken.Said == 1 && broken.LastError == "нет голоса");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void SayTaskTest()
        {
            FakeWorld w = new FakeWorld();
            GooseEntity g = w.NewGoose(new Vector2(200f, 500f));
            Deluxe.Goose = g;
            Input.mouseX = 900; Input.mouseY = 300;
            int arrived = 0;
            bool talking = true;
            SayTask.Arrived = () => arrived++;
            SayTask.StillTalking = () => talking;
            SayTask.Approach = true;
            Check("«сказать фразу» — задача гуся", Deluxe.SetTask(SayTask.Id, false) && w.TaskOf(g) == SayTask.Id);
            w.Run(g, 0.3f);
            Check("сам — сначала бежит к курсору и молчит", arrived == 0 && g.currentSpeed >= 199f);
            w.Run(g, 4.5f, () => arrived > 0);
            Vector2 spot = SayTask.SpotBeside(new Vector2(200f, 500f), new Vector2(900f, 300f), FakeWorld.Screen);
            Check("встал сбоку от курсора — и заговорил", arrived == 1 && Vector2.Distance(g.position, spot) < 20f,
                  g.position.x.ToString("0") + "," + g.position.y.ToString("0"));
            Vector2 at = g.position;
            w.Run(g, 2f);
            Check("пока говорит — стоит", Vector2.Distance(g.position, at) < 1f && w.TaskOf(g) == SayTask.Id);
            talking = false;
            w.Run(g, 1.5f, () => w.TaskOf(g) == "Wander");
            Check("договорил — гуляет дальше", w.TaskOf(g) == "Wander");

            SayTask.Approach = false;
            arrived = 0;
            talking = true;
            Deluxe.SetTask(SayTask.Id, false);
            w.Run(g, 0.1f);
            Check("из меню — говорит сразу, где стоит", arrived == 1);
            talking = false;
            w.Run(g, 1f, () => w.TaskOf(g) == "Wander");

            Input.mouseX = 5; Input.mouseY = 5;
            Vector2 corner = SayTask.SpotBeside(new Vector2(600, 400), new Vector2(5, 5), FakeWorld.Screen);
            Check("курсор в углу — гусь встаёт так, что облачку есть место", corner.x >= 40 && corner.y >= 150);
        }
    }
}
