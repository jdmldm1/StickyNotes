using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace StickyNotes__
{
    public partial class GraphWindow : Window
    {
        private readonly MainWindow _mainWnd;

        private readonly List<GraphNode> _nodes = new();
        private readonly List<GraphEdge> _edges = new();
        private List<string> _allTags = new();

        private bool _isInitialized = false;
        private Point _panStart;
        private bool _isPanning;
        private string? _highlightedTag;
        private GraphNode? _hoveredNode;
        private string _searchQuery = "";

        private static readonly Dictionary<string, Color> NoteColorMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "yellow",   Color.FromRgb(0xD4, 0x9A, 0x13) },
            { "green",    Color.FromRgb(0x1A, 0x8F, 0x54) },
            { "pink",     Color.FromRgb(0xC2, 0x18, 0x5B) },
            { "purple",   Color.FromRgb(0x7B, 0x1F, 0xA2) },
            { "blue",     Color.FromRgb(0x02, 0x88, 0xD1) },
            { "charcoal", Color.FromRgb(0x42, 0x42, 0x42) },
        };

        private class GraphNode
        {
            public int NoteId;
            public string Title = "";
            public string ColorName = "yellow";
            public string PlainText = "";
            public bool IsSecure;
            public double X, Y;
            public double Radius = 20;
            public int ConnectionCount;
            public HashSet<int> ConnectedNodeIds = new();
            public List<string> Tags = new();
            public Ellipse? Shape;
            public Ellipse? Halo;
            public TextBlock? Label;
        }

        private class GraphEdge
        {
            public GraphNode A = null!;
            public GraphNode B = null!;
            public bool IsWikiLink;
            public List<string> SharedTags = new();
            public Line? Line;
            public TextBlock? TagLabel;
        }

        public GraphWindow(MainWindow mainWnd)
        {
            InitializeComponent();
            _mainWnd = mainWnd;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitialized = true;
            BuildGraph();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _isInitialized = false;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void BuildGraph()
        {
            if (!_isInitialized || GraphCanvas == null) return;

            try
            {
                _nodes.Clear();
                _edges.Clear();
                GraphCanvas.Children.Clear();
                TagLegendPanel.Children.Clear();
                _hoveredNode = null;
                _highlightedTag = null;

                var allNotes = DatabaseHelper.ListNotes(includeContent: false);
                if (allNotes.Count == 0)
                {
                    if (StatusText != null)
                        StatusText.Text = "No notes found yet. Create notes in StickyNotes++ to view your knowledge graph!";
                    return;
                }

                var pairs = DatabaseHelper.GetAllNoteTagPairs();
                var noteTags = new Dictionary<int, HashSet<string>>();
                foreach (var (noteId, _, _, tag) in pairs)
                {
                    if (!noteTags.ContainsKey(noteId)) noteTags[noteId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    noteTags[noteId].Add(tag);
                }

                foreach (var note in allNotes)
                {
                    string displayTitle = note.IsSecure
                        ? (string.IsNullOrEmpty(note.Title) ? "🔒 Secure Note" : $"🔒 {note.Title}")
                        : (string.IsNullOrEmpty(note.Title) ? $"Note {note.Id}" : note.Title);

                    var node = new GraphNode
                    {
                        NoteId = note.Id,
                        Title = displayTitle,
                        ColorName = note.Color ?? "yellow",
                        PlainText = note.PlainText ?? "",
                        IsSecure = note.IsSecure
                    };

                    if (noteTags.TryGetValue(note.Id, out var tags))
                    {
                        node.Tags = tags.OrderBy(t => t).ToList();
                    }
                    _nodes.Add(node);
                }

                var nodeById = _nodes.ToDictionary(n => n.NoteId);

                var edgeMap = new Dictionary<(int, int), GraphEdge>();
                var rawConnections = DatabaseHelper.GetNoteConnections();
                foreach (var conn in rawConnections)
                {
                    if (nodeById.TryGetValue(conn.FromNoteId, out var a) && nodeById.TryGetValue(conn.ToNoteId, out var b) && a.NoteId != b.NoteId)
                    {
                        int minId = Math.Min(a.NoteId, b.NoteId);
                        int maxId = Math.Max(a.NoteId, b.NoteId);
                        if (!edgeMap.TryGetValue((minId, maxId), out var edge))
                        {
                            edge = new GraphEdge { A = a, B = b, IsWikiLink = true };
                            edgeMap[(minId, maxId)] = edge;
                        }
                        else
                        {
                            edge.IsWikiLink = true;
                        }
                        a.ConnectedNodeIds.Add(b.NoteId);
                        b.ConnectedNodeIds.Add(a.NoteId);
                    }
                }

                for (int i = 0; i < _nodes.Count; i++)
                {
                    for (int j = i + 1; j < _nodes.Count; j++)
                    {
                        var a = _nodes[i];
                        var b = _nodes[j];
                        if (!noteTags.ContainsKey(a.NoteId) || !noteTags.ContainsKey(b.NoteId)) continue;
                        var shared = noteTags[a.NoteId].Intersect(noteTags[b.NoteId], StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
                        if (shared.Count > 0)
                        {
                            int minId = Math.Min(a.NoteId, b.NoteId);
                            int maxId = Math.Max(a.NoteId, b.NoteId);
                            if (!edgeMap.TryGetValue((minId, maxId), out var edge))
                            {
                                edge = new GraphEdge { A = a, B = b, SharedTags = shared };
                                edgeMap[(minId, maxId)] = edge;
                            }
                            else
                            {
                                edge.SharedTags = shared;
                            }
                            a.ConnectedNodeIds.Add(b.NoteId);
                            b.ConnectedNodeIds.Add(a.NoteId);
                        }
                    }
                }

                _edges.AddRange(edgeMap.Values);

                foreach (var node in _nodes)
                {
                    node.ConnectionCount = node.ConnectedNodeIds.Count;
                    node.Radius = Math.Min(34.0, Math.Max(15.0, 14.0 + Math.Sqrt(node.ConnectionCount) * 4.5));
                }

                double width = GraphCanvasBorder.ActualWidth > 0 ? GraphCanvasBorder.ActualWidth : 960;
                double height = GraphCanvasBorder.ActualHeight > 0 ? GraphCanvasBorder.ActualHeight : 560;
                double cx = width / 2;
                double cy = height / 2;

                var sortedByDegree = _nodes.OrderByDescending(n => n.ConnectionCount).ToList();
                double goldenAngle = Math.PI * (3 - Math.Sqrt(5));
                for (int i = 0; i < sortedByDegree.Count; i++)
                {
                    double r = Math.Sqrt(i + 1) * Math.Min(cx, cy) * 0.28;
                    double theta = i * goldenAngle;
                    sortedByDegree[i].X = cx + r * Math.Cos(theta);
                    sortedByDegree[i].Y = cy + r * Math.Sin(theta);
                }

                _allTags = noteTags.Values.SelectMany(s => s).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
                BuildTagLegend(_allTags);

                Task.Run(() =>
                {
                    try
                    {
                        RunForceLayout(width, height);
                    }
                    catch { }
                }).ContinueWith(_ =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (_isInitialized && IsLoaded && GraphCanvas != null)
                        {
                            RenderGraph();
                        }
                    });
                }, TaskScheduler.Default);

                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                if (StatusText != null) StatusText.Text = "Notice: " + ex.Message;
            }
        }

        private void UpdateStatusBar()
        {
            if (StatusText == null) return;
            int wikilinkCount = _edges.Count(e => e.IsWikiLink);
            int tagLinkCount = _edges.Count(e => e.SharedTags.Count > 0);
            StatusText.Text = $"{_nodes.Count} notes · {_edges.Count} links ({wikilinkCount} wikilinks, {tagLinkCount} tag links) · {_allTags.Count} tags · Tip: Hover to inspect, drag to arrange, click to open";
        }

        private void BuildTagLegend(List<string> tags)
        {
            if (TagLegendPanel == null) return;
            TagLegendPanel.Children.Clear();
            foreach (var tag in tags)
            {
                var pill = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(50, 0, 132, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(100, 0, 132, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 5, 0),
                    Cursor = Cursors.Hand,
                    Tag = tag,
                };
                pill.Child = new TextBlock
                {
                    Text = "#" + tag,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xCC, 0xFF)),
                    FontSize = 11,
                };
                pill.MouseLeftButtonDown += TagPill_Click;
                TagLegendPanel.Children.Add(pill);
            }
        }

        private void TagPill_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border pill)
            {
                string tag = (string)pill.Tag;
                _highlightedTag = _highlightedTag == tag ? null : tag;

                foreach (UIElement child in TagLegendPanel.Children)
                {
                    if (child is Border b)
                    {
                        bool isSelected = (string)b.Tag == _highlightedTag;
                        b.Background = isSelected
                            ? new SolidColorBrush(Color.FromArgb(140, 0, 132, 255))
                            : new SolidColorBrush(Color.FromArgb(50, 0, 132, 255));
                    }
                }

                ApplySearchAndFilters();
            }
        }

        private void RunForceLayout(double width, double height)
        {
            if (_nodes.Count <= 1) return;

            const int iterations = 120;
            double k = Math.Max(140.0, Math.Sqrt((width * height) / _nodes.Count) * 0.95);
            double temp = width / 7.0;
            double cooling = temp / (iterations + 1);

            double cx = width / 2;
            double cy = height / 2;

            for (int iter = 0; iter < iterations; iter++)
            {
                var disps = new (double dx, double dy)[_nodes.Count];

                for (int i = 0; i < _nodes.Count; i++)
                {
                    for (int j = 0; j < _nodes.Count; j++)
                    {
                        if (i == j) continue;
                        double dx = _nodes[i].X - _nodes[j].X;
                        double dy = _nodes[i].Y - _nodes[j].Y;
                        double dist = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy));
                        double force = (k * k) / dist;

                        double minGap = _nodes[i].Radius + _nodes[j].Radius + 22.0;
                        if (dist < minGap) force *= 2.6;

                        disps[i].dx += (dx / dist) * force;
                        disps[i].dy += (dy / dist) * force;
                    }
                }

                foreach (var edge in _edges)
                {
                    int ai = _nodes.IndexOf(edge.A);
                    int bi = _nodes.IndexOf(edge.B);
                    if (ai < 0 || bi < 0) continue;

                    double dx = edge.A.X - edge.B.X;
                    double dy = edge.A.Y - edge.B.Y;
                    double dist = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy));
                    double stiffness = edge.IsWikiLink ? 1.35 : 1.0;
                    double force = (dist * dist) / (k * 1.2) * stiffness;
                    double fx = (dx / dist) * force;
                    double fy = (dy / dist) * force;

                    disps[ai].dx -= fx;
                    disps[ai].dy -= fy;
                    disps[bi].dx += fx;
                    disps[bi].dy += fy;
                }

                for (int i = 0; i < _nodes.Count; i++)
                {
                    disps[i].dx -= (_nodes[i].X - cx) * 0.025;
                    disps[i].dy -= (_nodes[i].Y - cy) * 0.025;
                }

                for (int i = 0; i < _nodes.Count; i++)
                {
                    double dmag = Math.Max(0.1, Math.Sqrt(disps[i].dx * disps[i].dx + disps[i].dy * disps[i].dy));
                    _nodes[i].X += (disps[i].dx / dmag) * Math.Min(dmag, temp);
                    _nodes[i].Y += (disps[i].dy / dmag) * Math.Min(dmag, temp);
                    _nodes[i].X = Math.Max(50, Math.Min(width - 50, _nodes[i].X));
                    _nodes[i].Y = Math.Max(40, Math.Min(height - 40, _nodes[i].Y));
                }

                temp -= cooling;
            }
        }

        private void RenderGraph()
        {
            if (!_isInitialized || GraphCanvas == null) return;
            GraphCanvas.Children.Clear();

            bool showLabels = ShowLabelsCheck?.IsChecked != false;
            bool showWikilinks = ShowWikilinksCheck?.IsChecked != false;
            bool showTags = ShowTagsCheck?.IsChecked != false;

            foreach (var edge in _edges)
            {
                bool edgeAllowed = (edge.IsWikiLink && showWikilinks) || (edge.SharedTags.Count > 0 && showTags);

                Color edgeColor;
                double strokeThickness;
                DoubleCollection? dashArray = null;

                if (edge.IsWikiLink && edge.SharedTags.Count > 0)
                {
                    edgeColor = Color.FromRgb(0x38, 0xBD, 0xF8);
                    strokeThickness = 2.2;
                }
                else if (edge.IsWikiLink)
                {
                    edgeColor = Color.FromRgb(0x00, 0xD2, 0xFF);
                    strokeThickness = 1.8;
                }
                else
                {
                    edgeColor = Color.FromRgb(0xC0, 0x84, 0xFC);
                    strokeThickness = 1.3;
                    dashArray = new DoubleCollection { 3, 2 };
                }

                var line = new Line
                {
                    X1 = edge.A.X, Y1 = edge.A.Y,
                    X2 = edge.B.X, Y2 = edge.B.Y,
                    Stroke = new SolidColorBrush(edgeColor),
                    StrokeThickness = strokeThickness,
                    StrokeDashArray = dashArray,
                    Opacity = 0.75,
                    Visibility = edgeAllowed ? Visibility.Visible : Visibility.Collapsed
                };
                edge.Line = line;
                GraphCanvas.Children.Add(line);

                if (edge.SharedTags.Count > 0)
                {
                    string labelText = edge.SharedTags.Count <= 2
                        ? string.Join(", ", edge.SharedTags.Select(t => "#" + t))
                        : $"{edge.SharedTags.Count} tags";

                    var edgeLabel = new TextBlock
                    {
                        Text = labelText,
                        Foreground = new SolidColorBrush(Color.FromArgb(160, 192, 132, 252)),
                        FontSize = 8.5,
                        Visibility = (showLabels && edgeAllowed) ? Visibility.Visible : Visibility.Collapsed,
                        IsHitTestVisible = false
                    };
                    edge.TagLabel = edgeLabel;
                    Canvas.SetLeft(edgeLabel, (edge.A.X + edge.B.X) / 2);
                    Canvas.SetTop(edgeLabel, (edge.A.Y + edge.B.Y) / 2);
                    GraphCanvas.Children.Add(edgeLabel);
                }
            }

            foreach (var node in _nodes)
            {
                Color nodeColor = NoteColorMap.TryGetValue(node.ColorName, out Color c) ? c : NoteColorMap["yellow"];
                double r = node.Radius;

                var halo = new Ellipse
                {
                    Width = (r + 7) * 2,
                    Height = (r + 7) * 2,
                    Stroke = new SolidColorBrush(Color.FromArgb(50, nodeColor.R, nodeColor.G, nodeColor.B)),
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                    Opacity = node.ConnectionCount >= 2 ? 0.8 : 0.0
                };
                node.Halo = halo;
                Canvas.SetLeft(halo, node.X - (r + 7));
                Canvas.SetTop(halo, node.Y - (r + 7));
                GraphCanvas.Children.Add(halo);

                var circle = new Ellipse
                {
                    Width = r * 2,
                    Height = r * 2,
                    Fill = new RadialGradientBrush(
                        Color.FromArgb(235, nodeColor.R, nodeColor.G, nodeColor.B),
                        Color.FromArgb(140, (byte)(nodeColor.R / 2), (byte)(nodeColor.G / 2), (byte)(nodeColor.B / 2))),
                    Stroke = new SolidColorBrush(Color.FromArgb(210, nodeColor.R, nodeColor.G, nodeColor.B)),
                    StrokeThickness = 2,
                    Cursor = Cursors.Hand,
                    Tag = node,
                    ToolTip = $"{node.Title}\nConnections: {node.ConnectionCount} (Wikilinks + Tags)\nTags: {(node.Tags.Count > 0 ? string.Join(", ", node.Tags.Select(t => "#" + t)) : "none")}\n\nDouble-click to open note"
                };

                circle.MouseEnter += (s, e) => OnNodeHover(node);
                circle.MouseLeave += (s, e) => OnNodeUnhover(node);
                circle.MouseLeftButtonDown += Node_MouseLeftButtonDown;
                circle.MouseLeftButtonUp += Node_MouseLeftButtonUp;
                circle.MouseMove += Node_MouseMove;
                node.Shape = circle;

                Canvas.SetLeft(circle, node.X - r);
                Canvas.SetTop(circle, node.Y - r);
                GraphCanvas.Children.Add(circle);

                string labelText = node.Title.Length > 16 ? node.Title.Substring(0, 14) + "…" : node.Title;
                var label = new TextBlock
                {
                    Text = labelText,
                    Foreground = Brushes.White,
                    FontSize = 9.5,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false,
                    Visibility = showLabels ? Visibility.Visible : Visibility.Collapsed
                };
                node.Label = label;
                Canvas.SetLeft(label, node.X - 45);
                label.Width = 90;
                Canvas.SetTop(label, node.Y + r + 3);
                GraphCanvas.Children.Add(label);
            }

            ApplySearchAndFilters();
        }

        private void OnNodeHover(GraphNode hoveredNode)
        {
            if (!_isInitialized) return;
            _hoveredNode = hoveredNode;

            foreach (var node in _nodes)
            {
                bool isTarget = node == hoveredNode;
                bool isNeighbor = hoveredNode.ConnectedNodeIds.Contains(node.NoteId);
                double op = (isTarget || isNeighbor) ? 1.0 : 0.12;

                if (node.Shape != null)
                {
                    node.Shape.Opacity = op;
                    if (isTarget)
                    {
                        node.Shape.StrokeThickness = 3.5;
                        node.Shape.Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                    }
                }
                if (node.Halo != null)
                {
                    node.Halo.Opacity = isTarget ? 1.0 : (isNeighbor ? 0.6 : 0.0);
                }
                if (node.Label != null)
                {
                    node.Label.Opacity = (isTarget || isNeighbor) ? 1.0 : 0.1;
                }
            }

            foreach (var edge in _edges)
            {
                bool connected = edge.A == hoveredNode || edge.B == hoveredNode;
                if (edge.Line != null)
                {
                    edge.Line.Opacity = connected ? 1.0 : 0.05;
                    edge.Line.StrokeThickness = connected ? 2.8 : 1.0;
                }
                if (edge.TagLabel != null)
                {
                    edge.TagLabel.Opacity = connected ? 1.0 : 0.05;
                }
            }

            string tagsDesc = hoveredNode.Tags.Count > 0 ? string.Join(" ", hoveredNode.Tags.Select(t => "#" + t)) : "No tags";
            if (StatusText != null)
                StatusText.Text = $"📌 \"{hoveredNode.Title}\" · {hoveredNode.ConnectionCount} links · {tagsDesc} · Click or double-click to open";
        }

        private void OnNodeUnhover(GraphNode hoveredNode)
        {
            if (_hoveredNode == hoveredNode)
            {
                _hoveredNode = null;
                ApplySearchAndFilters();
            }
        }

        private void SearchGraphBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            _searchQuery = SearchGraphBox.Text.Trim();
            bool hasQuery = !string.IsNullOrEmpty(_searchQuery);
            if (SearchPlaceholderText != null)
                SearchPlaceholderText.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;
            if (ClearSearchBtn != null)
                ClearSearchBtn.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
            ApplySearchAndFilters();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchGraphBox.Text = "";
            SearchGraphBox.Focus();
        }

        private void SearchGraphBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchGraphBox.Text = "";
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                FocusFirstMatch();
                e.Handled = true;
            }
        }

        private void FocusFirstMatch()
        {
            if (string.IsNullOrEmpty(_searchQuery) || GraphCanvasBorder == null) return;
            var match = _nodes.FirstOrDefault(n => MatchesSearch(n, _searchQuery));
            if (match != null)
            {
                double w = GraphCanvasBorder.ActualWidth > 0 ? GraphCanvasBorder.ActualWidth : 960;
                double h = GraphCanvasBorder.ActualHeight > 0 ? GraphCanvasBorder.ActualHeight : 560;
                GraphTranslate.X = (w / 2) - match.X * GraphScale.ScaleX;
                GraphTranslate.Y = (h / 2) - match.Y * GraphScale.ScaleY;
                OnNodeHover(match);
            }
        }

        private static bool MatchesSearch(GraphNode node, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            if (node.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            if (node.PlainText.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            return node.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplySearchAndFilters()
        {
            if (!_isInitialized) return;

            bool showWikilinks = ShowWikilinksCheck?.IsChecked != false;
            bool showTags = ShowTagsCheck?.IsChecked != false;
            bool showLabels = ShowLabelsCheck?.IsChecked != false;
            bool hasQuery = !string.IsNullOrEmpty(_searchQuery);

            int matchCount = 0;
            foreach (var node in _nodes)
            {
                bool matches = !hasQuery || MatchesSearch(node, _searchQuery);
                bool matchesTag = _highlightedTag == null || node.Tags.Contains(_highlightedTag, StringComparer.OrdinalIgnoreCase);

                bool visible = matches && matchesTag;
                if (visible) matchCount++;

                double opacity = visible ? 1.0 : 0.12;
                if (node.Shape != null)
                {
                    node.Shape.Opacity = opacity;
                    if (hasQuery && matches)
                    {
                        node.Shape.Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                        node.Shape.StrokeThickness = 3;
                    }
                    else
                    {
                        Color nodeColor = NoteColorMap.TryGetValue(node.ColorName, out Color c) ? c : NoteColorMap["yellow"];
                        node.Shape.Stroke = new SolidColorBrush(Color.FromArgb(210, nodeColor.R, nodeColor.G, nodeColor.B));
                        node.Shape.StrokeThickness = 2;
                    }
                }
                if (node.Halo != null)
                {
                    node.Halo.Opacity = (visible && (node.ConnectionCount >= 2 || (hasQuery && matches))) ? 0.8 : 0.0;
                }
                if (node.Label != null)
                {
                    node.Label.Visibility = showLabels ? Visibility.Visible : Visibility.Collapsed;
                    node.Label.Opacity = visible ? 1.0 : 0.1;
                }
            }

            foreach (var edge in _edges)
            {
                bool edgeAllowed = (edge.IsWikiLink && showWikilinks) || (edge.SharedTags.Count > 0 && showTags);
                if (!edgeAllowed)
                {
                    if (edge.Line != null) edge.Line.Visibility = Visibility.Collapsed;
                    if (edge.TagLabel != null) edge.TagLabel.Visibility = Visibility.Collapsed;
                    continue;
                }

                if (edge.Line != null) edge.Line.Visibility = Visibility.Visible;
                if (edge.TagLabel != null) edge.TagLabel.Visibility = showLabels && edge.SharedTags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                bool aMatch = (!hasQuery || MatchesSearch(edge.A, _searchQuery)) && (_highlightedTag == null || edge.A.Tags.Contains(_highlightedTag, StringComparer.OrdinalIgnoreCase));
                bool bMatch = (!hasQuery || MatchesSearch(edge.B, _searchQuery)) && (_highlightedTag == null || edge.B.Tags.Contains(_highlightedTag, StringComparer.OrdinalIgnoreCase));

                bool tagMatch = _highlightedTag == null || edge.SharedTags.Contains(_highlightedTag, StringComparer.OrdinalIgnoreCase);

                bool edgeActive = tagMatch && (aMatch && bMatch);
                double edgeOpacity = edgeActive ? 0.85 : 0.08;

                if (edge.Line != null)
                {
                    edge.Line.Opacity = edgeOpacity;
                    edge.Line.StrokeThickness = edge.IsWikiLink ? 1.8 : 1.3;
                }
                if (edge.TagLabel != null) edge.TagLabel.Opacity = edgeOpacity;
            }

            if (StatusText != null)
            {
                if (hasQuery)
                {
                    StatusText.Text = $"Found {matchCount} note{(matchCount == 1 ? "" : "s")} matching \"{_searchQuery}\" · Press Enter to center on match";
                }
                else if (_highlightedTag != null)
                {
                    StatusText.Text = $"Filtered by #{_highlightedTag} ({matchCount} notes) · Click tag pill again to clear";
                }
                else
                {
                    UpdateStatusBar();
                }
            }
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplySearchAndFilters();
        }

        private void ShowLabels_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplySearchAndFilters();
        }

        private GraphNode? _draggingNode;
        private Point _dragOffset;
        private bool _nodeDragged;

        private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse el && el.Tag is GraphNode node)
            {
                _draggingNode = node;
                _nodeDragged = false;
                _dragOffset = e.GetPosition(GraphCanvas);
                _dragOffset.X -= node.X;
                _dragOffset.Y -= node.Y;
                el.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingNode == null || e.LeftButton != MouseButtonState.Pressed) return;
            _nodeDragged = true;
            Point pos = e.GetPosition(GraphCanvas);
            _draggingNode.X = pos.X - _dragOffset.X;
            _draggingNode.Y = pos.Y - _dragOffset.Y;
            UpdateNodePosition(_draggingNode);
            e.Handled = true;
        }

        private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingNode != null)
            {
                if (sender is Ellipse el) el.ReleaseMouseCapture();
                if (!_nodeDragged)
                {
                    _mainWnd.OpenNoteWindow(_draggingNode.NoteId);
                }
                _draggingNode = null;
            }
        }

        private void UpdateNodePosition(GraphNode node)
        {
            double r = node.Radius;
            if (node.Shape != null)
            {
                Canvas.SetLeft(node.Shape, node.X - r);
                Canvas.SetTop(node.Shape, node.Y - r);
            }
            if (node.Halo != null)
            {
                Canvas.SetLeft(node.Halo, node.X - (r + 7));
                Canvas.SetTop(node.Halo, node.Y - (r + 7));
            }
            if (node.Label != null)
            {
                Canvas.SetLeft(node.Label, node.X - 45);
                Canvas.SetTop(node.Label, node.Y + r + 3);
            }
            foreach (var edge in _edges.Where(e => e.A == node || e.B == node))
            {
                if (edge.Line != null)
                {
                    edge.Line.X1 = edge.A.X; edge.Line.Y1 = edge.A.Y;
                    edge.Line.X2 = edge.B.X; edge.Line.Y2 = edge.B.Y;
                }
                if (edge.TagLabel != null)
                {
                    Canvas.SetLeft(edge.TagLabel, (edge.A.X + edge.B.X) / 2);
                    Canvas.SetTop(edge.TagLabel, (edge.A.Y + edge.B.Y) / 2);
                }
            }
        }

        private void GraphBorder_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.1 : 0.9;
            GraphScale.ScaleX = Math.Max(0.2, Math.Min(4.0, GraphScale.ScaleX * factor));
            GraphScale.ScaleY = Math.Max(0.2, Math.Min(4.0, GraphScale.ScaleY * factor));
        }

        private void GraphBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_draggingNode != null) return;
            _isPanning = true;
            _panStart = e.GetPosition(GraphCanvasBorder);
            ((UIElement)sender).CaptureMouse();
        }

        private void GraphBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning || e.LeftButton != MouseButtonState.Pressed) return;
            Point pos = e.GetPosition(GraphCanvasBorder);
            GraphTranslate.X += pos.X - _panStart.X;
            GraphTranslate.Y += pos.Y - _panStart.Y;
            _panStart = pos;
        }

        private void GraphBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isPanning = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }

        private void ResetLayout_Click(object sender, RoutedEventArgs e)
        {
            GraphScale.ScaleX = GraphScale.ScaleY = 1;
            GraphTranslate.X = GraphTranslate.Y = 0;
            BuildGraph();
        }
    }
}
