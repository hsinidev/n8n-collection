using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using N8nWorkflowHub.Models;
using N8nWorkflowHub.Services;
using Path = System.IO.Path;

namespace N8nWorkflowHub
{
    public partial class MainWindow : Window
    {
        private readonly WorkflowDatabaseService _dbService;
        private List<CategoryItem> _allCategories = new();
        private WorkflowItem? _selectedWorkflow;
        private string _currentJson = string.Empty;
        private DispatcherTimer? _searchDebounceTimer;
        private bool _isInitialized = false;

        private const string SearchPlaceholder = "🔍 Search 22,500+ workflows by name, node or tag...";
        private const string CategoryPlaceholder = "Search categories...";

        public MainWindow()
        {
            InitializeComponent();
            _dbService = new WorkflowDatabaseService();

            try
            {
                var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/app_icon.ico"));
                if (iconStream != null)
                {
                    Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconStream.Stream);
                }
            }
            catch
            {
                // Fallback gracefully without crash
            }

            TxtSearchQuery.Text = SearchPlaceholder;
            TxtCategoryFilter.Text = CategoryPlaceholder;

            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _searchDebounceTimer.Tick += async (s, e) =>
            {
                _searchDebounceTimer.Stop();
                await ExecuteSearchAsync();
            };

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtStatus.Text = "Initializing embedded workflow database...";
                var progress = new Progress<string>(msg => TxtStatus.Text = msg);
                await _dbService.InitializeAsync(progress);

                _allCategories = await _dbService.GetCategoriesAsync();
                LstCategories.ItemsSource = _allCategories;
                _isInitialized = true;

                if (_allCategories.Count > 0)
                {
                    LstCategories.SelectedIndex = 0; // Select "All Workflows"
                }

                await ExecuteSearchAsync();
                TxtStatus.Text = $"Ready • 22,500 workflows indexed across {_allCategories.Count - 1} categories";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization Error: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "Error loading embedded database.";
            }
        }

        private async Task ExecuteSearchAsync()
        {
            if (!_isInitialized) return;

            try
            {
                string query = TxtSearchQuery.Text.Trim();
                if (query == SearchPlaceholder) query = "";

                string category = "All Workflows";
                if (LstCategories?.SelectedItem is CategoryItem catItem)
                {
                    category = catItem.Name;
                }

                string nodeFilter = "All Nodes";
                if (CmbNodeFilter?.SelectedItem is ComboBoxItem nodeItem && nodeItem.Content != null)
                {
                    nodeFilter = nodeItem.Content.ToString() ?? "All Nodes";
                }

                TxtResultsCount.Text = "Searching...";
                var results = await _dbService.SearchWorkflowsAsync(query, category, nodeFilter, limit: 500);

                LstWorkflows.ItemsSource = results;
                TxtResultsCount.Text = $"Showing {results.Count:N0} matching workflows";

                if (results.Count > 0)
                {
                    LstWorkflows.SelectedIndex = 0;
                }
                else
                {
                    ClearPreview();
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Search error: {ex.Message}";
            }
        }

        private void TxtSearchQuery_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtSearchQuery.Text == SearchPlaceholder)
            {
                TxtSearchQuery.Text = "";
            }
        }

        private void TxtSearchQuery_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSearchQuery.Text))
            {
                TxtSearchQuery.Text = SearchPlaceholder;
            }
        }

        private void TxtSearchQuery_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _searchDebounceTimer == null) return;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void TxtCategoryFilter_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtCategoryFilter.Text == CategoryPlaceholder)
            {
                TxtCategoryFilter.Text = "";
            }
        }

        private void TxtCategoryFilter_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtCategoryFilter.Text))
            {
                TxtCategoryFilter.Text = CategoryPlaceholder;
            }
        }

        private void TxtCategoryFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _allCategories == null || LstCategories == null) return;

            string filter = TxtCategoryFilter.Text.Trim().ToLower();
            if (filter == CategoryPlaceholder.ToLower() || string.IsNullOrWhiteSpace(filter))
            {
                LstCategories.ItemsSource = _allCategories;
            }
            else
            {
                LstCategories.ItemsSource = _allCategories.Where(c => c.Name.ToLower().Contains(filter)).ToList();
            }
        }

        private async void LstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            await ExecuteSearchAsync();
        }

        private async void CmbNodeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            await ExecuteSearchAsync();
        }

        private async void LstWorkflows_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstWorkflows.SelectedItem is WorkflowItem item)
            {
                _selectedWorkflow = item;
                TxtSelectedTitle.Text = item.Name;
                TxtSelectedCategory.Text = $"Category: {item.Category}";
                TxtSelectedNodeCount.Text = $"Nodes: {item.NodeCount}";
                TxtSelectedId.Text = $"ID: {item.Id}";

                TxtJsonViewer.Text = "Decompressing workflow JSON from embedded store...";

                try
                {
                    string? json = await _dbService.GetWorkflowJsonAsync(item.Id);
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(json);
                            _currentJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                            
                            // Render the Visual Architecture Canvas
                            RenderVisualWorkflowDiagram(doc.RootElement);
                        }
                        catch
                        {
                            _currentJson = json;
                            PnlVisualCanvas.Children.Clear();
                        }

                        TxtJsonViewer.Text = _currentJson;
                        TxtStatus.Text = $"Loaded: {item.Name} ({item.Category})";
                    }
                    else
                    {
                        TxtJsonViewer.Text = "// Error: JSON not found.";
                        PnlVisualCanvas.Children.Clear();
                    }
                }
                catch (Exception ex)
                {
                    TxtJsonViewer.Text = $"// Error decompressing JSON: {ex.Message}";
                    PnlVisualCanvas.Children.Clear();
                }
            }
        }

        private void RenderVisualWorkflowDiagram(JsonElement root)
        {
            PnlVisualCanvas.Children.Clear();

            if (!root.TryGetProperty("nodes", out var nodesElement) || nodesElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var nodeCards = new List<(string name, double x, double y, Border element)>();
            int nodeIndex = 0;
            double startX = 60;
            double startY = 80;
            double xGap = 220;
            double yGap = 130;

            foreach (var node in nodesElement.EnumerateArray())
            {
                string name = node.TryGetProperty("name", out var n) ? n.GetString() ?? $"Node {nodeIndex+1}" : $"Node {nodeIndex+1}";
                string type = node.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                
                // Get node color & icon
                var (icon, colorHex) = GetNodeVisualProps(type);
                var nodeColor = (Color)ColorConverter.ConvertFromString(colorHex);

                // Position calculation
                double posX = startX + (nodeIndex % 4) * xGap;
                double posY = startY + (nodeIndex / 4) * yGap;

                if (node.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Array && pos.GetArrayLength() >= 2)
                {
                    // Scale position if custom
                    double customX = pos[0].GetDouble();
                    double customY = pos[1].GetDouble();
                    if (customX > 0 && customY > 0)
                    {
                        posX = Math.Max(40, customX * 0.75);
                        posY = Math.Max(40, customY * 0.65);
                    }
                }

                // Create sleek Node Card Border
                var card = new Border
                {
                    Width = 175,
                    Height = 78,
                    Background = new SolidColorBrush(Color.FromArgb(240, 19, 29, 49)),
                    BorderBrush = new SolidColorBrush(nodeColor),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8, 10, 8),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = nodeColor,
                        BlurRadius = 14,
                        ShadowDepth = 0,
                        Opacity = 0.35
                    }
                };

                var sp = new StackPanel();

                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var iconBox = new Border
                {
                    Width = 22,
                    Height = 22,
                    Background = new SolidColorBrush(nodeColor),
                    CornerRadius = new CornerRadius(6)
                };
                iconBox.Child = new TextBlock
                {
                    Text = icon,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBox, 0);
                headerGrid.Children.Add(iconBox);

                var titleBlock = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(titleBlock, 1);
                headerGrid.Children.Add(titleBlock);

                sp.Children.Add(headerGrid);

                string typeShort = type.Replace("n8n-nodes-base.", "");
                var typeBlock = new TextBlock
                {
                    Text = typeShort,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    Margin = new Thickness(0, 4, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                sp.Children.Add(typeBlock);

                var statusBlock = new TextBlock
                {
                    Text = "● ACTIVE STEP",
                    FontSize = 8,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(nodeColor),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                sp.Children.Add(statusBlock);

                card.Child = sp;

                Canvas.SetLeft(card, posX);
                Canvas.SetTop(card, posY);
                PnlVisualCanvas.Children.Add(card);

                nodeCards.Add((name, posX, posY, card));
                nodeIndex++;
            }

            // Draw connection curves between sequential nodes
            for (int i = 0; i < nodeCards.Count - 1; i++)
            {
                var src = nodeCards[i];
                var dst = nodeCards[i + 1];

                double x1 = src.x + 175;
                double y1 = src.y + 39;
                double x2 = dst.x;
                double y2 = dst.y + 39;

                var path = new System.Windows.Shapes.Path
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(180, 56, 189, 248)),
                    StrokeThickness = 2.2,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };

                var geometry = new PathGeometry();
                var figure = new PathFigure { StartPoint = new Point(x1, y1) };
                
                double controlOffset = Math.Abs(x2 - x1) / 2;
                var segment = new BezierSegment(
                    new Point(x1 + controlOffset, y1),
                    new Point(x2 - controlOffset, y2),
                    new Point(x2, y2),
                    true
                );
                figure.Segments.Add(segment);
                geometry.Figures.Add(figure);
                path.Data = geometry;

                // Add arrow indicator point
                var arrow = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(56, 189, 248))
                };
                Canvas.SetLeft(arrow, x2 - 4);
                Canvas.SetTop(arrow, y2 - 4);

                PnlVisualCanvas.Children.Add(path);
                PnlVisualCanvas.Children.Add(arrow);
            }

            // Expand canvas bounds if needed
            if (nodeCards.Count > 0)
            {
                double maxX = nodeCards.Max(n => n.x) + 300;
                double maxY = nodeCards.Max(n => n.y) + 200;
                PnlVisualCanvas.Width = Math.Max(1200, maxX);
                PnlVisualCanvas.Height = Math.Max(600, maxY);
            }
        }

        private (string icon, string colorHex) GetNodeVisualProps(string type)
        {
            string t = type.ToLower();
            if (t.Contains("webhook")) return ("⚡", "#10B981");
            if (t.Contains("openai") || t.Contains("ai")) return ("🤖", "#8B5CF6");
            if (t.Contains("schedule") || t.Contains("cron")) return ("⏰", "#F59E0B");
            if (t.Contains("postgres") || t.Contains("sql") || t.Contains("database")) return ("💾", "#3B82F6");
            if (t.Contains("slack")) return ("💬", "#EC4899");
            if (t.Contains("telegram")) return ("✈️", "#0284C7");
            if (t.Contains("stripe")) return ("💳", "#6366F1");
            if (t.Contains("shopify")) return ("🛍️", "#84CC16");
            if (t.Contains("code")) return ("💻", "#14B8A6");
            if (t.Contains("switch") || t.Contains("router")) return ("🔀", "#F97316");
            if (t.Contains("email")) return ("✉️", "#EF4444");
            return ("⚙️", "#38BDF8");
        }

        private void ClearPreview()
        {
            _selectedWorkflow = null;
            _currentJson = string.Empty;
            TxtSelectedTitle.Text = "No Matching Workflows";
            TxtSelectedCategory.Text = "Category: None";
            TxtSelectedNodeCount.Text = "Nodes: 0";
            TxtSelectedId.Text = "ID: -";
            TxtJsonViewer.Text = "// No workflow selected matching the current search criteria.";
            PnlVisualCanvas.Children.Clear();
        }

        private void BtnCopyJson_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentJson))
            {
                MessageBox.Show("Please select a workflow first to copy its JSON.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(_currentJson);
            TxtStatus.Text = "✅ Copied JSON to Clipboard! Paste directly into n8n canvas (Ctrl+V).";
            MessageBox.Show("Workflow JSON copied to clipboard!\n\nSimply open your n8n canvas and press Ctrl+V to paste the workflow directly.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSaveJson_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentJson) || _selectedWorkflow == null)
            {
                MessageBox.Show("Please select a workflow first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save n8n Workflow JSON",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"{SanitizeFileName(_selectedWorkflow.Name)}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, _currentJson, Encoding.UTF8);
                TxtStatus.Text = $"Saved workflow to: {dialog.FileName}";
                MessageBox.Show($"Workflow saved successfully to:\n{dialog.FileName}", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnExportCategory_Click(object sender, RoutedEventArgs e)
        {
            string category = "All Workflows";
            if (LstCategories.SelectedItem is CategoryItem catItem)
            {
                category = catItem.Name;
            }

            var dialog = new OpenFolderDialog
            {
                Title = $"Select Folder to Export '{category}' Workflows"
            };

            if (dialog.ShowDialog() == true)
            {
                string targetDir = dialog.FolderName;
                TxtStatus.Text = $"Exporting workflows for {category}...";

                try
                {
                    var items = await _dbService.GetCategoryWorkflowsAsync(category);
                    int exported = 0;

                    foreach (var (name, json) in items)
                    {
                        string safeName = SanitizeFileName(name) + ".json";
                        string outPath = Path.Combine(targetDir, safeName);
                        File.WriteAllText(outPath, json, Encoding.UTF8);
                        exported++;
                    }

                    TxtStatus.Text = $"Successfully exported {exported} workflows to {targetDir}";
                    MessageBox.Show($"Exported {exported} workflows into:\n{targetDir}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnPushN8n_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentJson) || _selectedWorkflow == null)
            {
                MessageBox.Show("Please select a workflow first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            TxtStatus.Text = "Attempting to push workflow to local n8n instance (http://localhost:5678)...";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var content = new StringContent(_currentJson, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("http://localhost:5678/api/v1/workflows", content);

                if (response.IsSuccessStatusCode)
                {
                    TxtStatus.Text = "✅ Successfully pushed workflow to local n8n instance!";
                    MessageBox.Show("Workflow imported into your running n8n instance successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TxtStatus.Text = $"n8n response: {response.StatusCode}. Use Copy JSON (Ctrl+V) instead.";
                    MessageBox.Show($"Could not automatically import (Status {response.StatusCode}).\n\nPlease click 'Copy JSON' and paste directly into n8n using Ctrl+V.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch
            {
                TxtStatus.Text = "Local n8n instance not reachable. Use Copy JSON (Ctrl+V) directly into canvas.";
                MessageBox.Show("Local n8n server is not currently running at http://localhost:5678.\n\nYou can click 'Copy JSON' and paste directly into your n8n browser canvas with Ctrl+V.", "n8n Offline", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnVisitPortfolio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://hsini.dev") { UseShellExecute = true });
            }
            catch
            {
                // Ignore
            }
        }

        private string SanitizeFileName(string name)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (char c in invalid)
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}