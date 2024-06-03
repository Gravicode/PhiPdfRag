﻿using PhiPdfRag.VectorDB;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Threading;
using GemBox.Pdf;

namespace PhiPdfRag;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly SLMRunner SLMRunner;
    private readonly RAGService RAGService;
    private CancellationTokenSource? cts;
    private List<uint>? selectedPages = null;
    private int selectedPageIndex = -1;
    private FileInfo pdfFile;
    
    [GeneratedRegex(@"[\u0000-\u001F\u007F-\uFFFF]")]
    private static partial Regex MyRegex();

    public MainWindow()
    {
        SLMRunner = new SLMRunner();
        SLMRunner.ModelLoaded += (sender, e) => Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => CheckIfReady(sender));

        RAGService = new RAGService();
        RAGService.ResourcesLoaded += (sender, e) => Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => CheckIfReady(sender));

        Closed += (sender, e) =>
        {
            cts?.Cancel();
            cts = null;
            SLMRunner?.Dispose();
            RAGService?.Dispose();
        };

        InitializeComponent();
        this.WindowState = WindowState.Maximized;
    }

    private void CheckIfReady(object? sender)
    {
        if (sender == RAGService)
        {
            IndexPDFButton.IsEnabled = RAGService.IsModelReady;
        }

        AskSLMButton.IsEnabled = SLMRunner.IsReady && RAGService.IsReady;
        if (RAGService.IsReady)
        {
            IndexPDFButton.Content = "Model Ready";
        }
    }

    private async void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        SearchTextBoxInitialText = SearchTextBox.Text;
        await Task.Run(() => Task.WhenAll(SLMRunner.InitializeAsync(), RAGService.InitializeAsync()));
    }

    private async void IndexPDFButton_Click(object sender, RoutedEventArgs e)
    {
        IndexPDFButton.IsEnabled = false;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            DefaultExt = ".pdf",
            Filter = "Pdf documents (.pdf)|*.pdf"
        };

        bool? result = dialog.ShowDialog();

        if (result != true)
        {
            IndexPDFButton.IsEnabled = RAGService.IsModelReady;
            return;
        }

        IndexPDFGrid.Visibility = Visibility.Collapsed;
        ChatGrid.Visibility = Visibility.Visible;
        IndexPDFProgressStackPanel.Visibility = Visibility.Visible;
        IndexPDFProgressBar.Minimum = 0;
        IndexPDFProgressBar.Maximum = 1;
        IndexPDFProgressBar.Value = 0;
        IndexPDFProgressTextBlock.Text = "Reading PDF...";

        ShowPDFPage.IsEnabled = true;
        Title = $"RAG with Phi 3 - {System.IO.Path.GetFileName(dialog.FileName)}";
        pdfFile = new FileInfo(dialog.FileName);//await StorageFile.GetFileFromPathAsync(dialog.FileName).AsTask().ConfigureAwait(false);

        var contents = new List<TextChunk>();
        // 1) Read the PDF file
        using (var document = UglyToad.PdfPig.PdfDocument.Open(pdfFile.FullName))
        {
            foreach (var page in document.GetPages())
            {
                var words = page.GetWords();
                var builder = string.Join(" ", words);

                var range = builder
                        .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => MyRegex().Replace(x, ""))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => new TextChunk
                        {
                            Text = x,
                            Page = page.Number,
                        });

                contents.AddRange(range);
            }
        }

        // 2) Split the text into chunks to make sure they are
        // smaller than what the Embeddings model supports
        var maxLength = 1024 / 2;
        for (int i = 0; i < contents.Count; i++)
        {
            var content = contents[i];
            int index = 0;
            var contentChunks = new List<TextChunk>();
            while (index < content.Text!.Length)
            {
                if (index + maxLength >= content.Text.Length)
                {
                    contentChunks.Add(new TextChunk(content)
                    {
                        Text = Regex.Replace(content.Text[index..].Trim(), @"(\.){2,}", ".")
                    });
                    break;
                }

                int lastIndexOfBreak = content.Text.LastIndexOf(' ', index + maxLength, maxLength);
                if (lastIndexOfBreak <= index)
                {
                    lastIndexOfBreak = index + maxLength;
                }

                contentChunks.Add(new TextChunk(content)
                {
                    Text = Regex.Replace(content.Text[index..lastIndexOfBreak].Trim(), @"(\.){2,}", ".")
                }); ;

                index = lastIndexOfBreak + 1;
            }

            contents.RemoveAt(i);
            contents.InsertRange(i, contentChunks);
            i += contentChunks.Count - 1;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IndexPDFProgressBar.Minimum = 0;
            IndexPDFProgressBar.Maximum = contents.Count;
            IndexPDFProgressBar.Value = 0;
        });

        Stopwatch sw = Stopwatch.StartNew();

        void UpdateProgress(float progress)
        {
            var elapsed = sw.Elapsed;
            if (progress == 0)
            {
                progress = 0.0001f;
            }

            var remaining = TimeSpan.FromSeconds((long)(elapsed.TotalSeconds / progress * (1 - progress) / 5) * 5);

            IndexPDFProgressBar.Value = progress * contents.Count;
            IndexPDFProgressTextBlock.Text = $"Indexing PDF... {progress:P0} ({remaining})";
        }

        if (cts != null)
        {
            cts.Cancel();
            cts = null;
            AskSLMButton.Content = "Answer";
            return;
        }

        cts = new CancellationTokenSource();

        // 3) Index the chunks
        await RAGService.InitializeAsync(contents, (sender, progress) =>
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                UpdateProgress(progress);
            });
        }, cts.Token);

        cts = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IndexPDFProgressTextBlock.Text = "Indexing PDF... Done!";
        });

        await Task.Delay(1000);

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            IndexPDFButton.IsEnabled = RAGService.IsModelReady;
            await Task.Delay(1000);
            IndexPDFProgressStackPanel.Visibility = Visibility.Collapsed;
        });
    }

    private async void AskSLMButton_Click(object sender, RoutedEventArgs e)
    {
        if (cts != null)
        {
            cts.Cancel();
            cts = null;
            AskSLMButton.Content = "Answer";
            return;
        }

        selectedPageIndex = 0;
        AskSLMButton.Content = "Cancel";
        cts = new CancellationTokenSource();

        var prompt = """
        <|system|>
        You are a helpful assistant, and you should answer questions about this information, in a direct and simple way, using only this content:
        """;

        // 4) Search the chunks using the user's prompt, with the same model used for indexing
        var contents = (await RAGService.Search(SearchTextBox.Text, 2)).OrderBy(c => c.Page);

        selectedPages = contents.Select(c => (uint)c.Page).Distinct().ToList();

        PagesUsedRun.Text = $"Using page(s) : {string.Join(", ", selectedPages)}";

        var pagesChunks = contents.GroupBy(c => c.Page)
            .Select(g => $"{Environment.NewLine}Page {g.Key}: {string.Join(Environment.NewLine, g.Select(c => c.Text))}").ToList();

        prompt += string.Join(Environment.NewLine, pagesChunks);

        prompt += $"""
        <|end|>
        <|user|>
        {SearchTextBox.Text}<|end|>
        <|assistant|>
        """;

        AnswerRun.Text = "";
        var fullResult = "";

        await Task.Run(async () =>
        {
            // 5) Use Phi3 to generate the answer
            await foreach (var partialResult in SLMRunner.InferStreamingAsync(prompt).WithCancellation(cts.Token))
            {
                fullResult += partialResult;
                await Application.Current.Dispatcher.InvokeAsync(() => AnswerRun.Text = fullResult);
            }
        }, cts.Token);

        cts = null;

        AskSLMButton.Content = "Answer";
    }

    private async void ShowPDFPage_Click(object sender, RoutedEventArgs e)
    {
        await UpdatePdfImageAsync().ConfigureAwait(false);
    }

    private async Task UpdatePdfImageAsync()
    {
        if (pdfFile == null || selectedPages == null || selectedPages.Count() == 0)
        {
            return;
        }
        // Load a PDF document.
        using (var pdfDocument = GemBox.Pdf.PdfDocument.Load(pdfFile.FullName))
        {
            // Create image save options.
            var imageOptions = new ImageSaveOptions(ImageSaveFormat.Jpeg)
            {
                PageNumber = 0, // Select the first PDF page.
                Width = 1240 // Set the image width and keep the aspect ratio.
            };

            //var pdfDocument = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(pdfFile).AsTask().ConfigureAwait(false);
            
            var pageId = selectedPages[selectedPageIndex];
            if (pageId < 0 || pdfDocument.Pages.Count < pageId)
            {
                return;
            }
            //var page = pdfDocument.Pages[pageId-1];
            //var page = pdfDocument.GetPage(pageId - 1);
            /*
            InMemoryRandomAccessStream inMemoryRandomAccessStream = new();
            var rect = page.Dimensions.TrimBox;
            await page.RenderToStreamAsync(inMemoryRandomAccessStream).AsTask().ConfigureAwait(false);
            */
            using (var imageStream = new MemoryStream())
            {
                imageOptions.PageNumber = (int)(pageId-1);
                pdfDocument.Save(imageStream, imageOptions);

                imageStream.Position = 0;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    BitmapImage bitmapImage = new();

                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = imageStream;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    PdfImage.Source = bitmapImage;
                    PageNumberTextBlock.Text = $"{pageId}/{pdfDocument.Pages.Count}";

                    PdfImageGrid.Visibility = Visibility.Visible;
                    UpdatePreviousAndNextPageButtonEnabled();
                });

            }

            // Save a PDF document to a JPEG file.
            //document.Save("Output.jpg", imageOptions);
        }
        
    }

    private void UpdatePreviousAndNextPageButtonEnabled()
    {
        if (selectedPages == null || selectedPages.Count == 0)
        {
            PreviousPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
            return;
        }

        PreviousPageButton.IsEnabled = selectedPageIndex > 0;
        NextPageButton.IsEnabled = selectedPageIndex < selectedPages.Count - 1;
    }

    private void PdfImage_Tapped(object sender, MouseButtonEventArgs e)
    {
        PdfImageGrid.Visibility = Visibility.Collapsed;
    }

    private async void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedPageIndex <= 0)
        {
            return;
        }
        selectedPageIndex--;
        await UpdatePdfImageAsync().ConfigureAwait(false);
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedPages == null || selectedPageIndex >= selectedPages.Count - 1)
        {
            return;
        }
        selectedPageIndex++;
        await UpdatePdfImageAsync().ConfigureAwait(false);
    }

    private string SearchTextBoxInitialText = string.Empty;

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchTextBox.Text == SearchTextBoxInitialText)
        {
            SearchTextBox.Text = string.Empty;
        }
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            SearchTextBox.Text = SearchTextBoxInitialText;
        }
    }
}