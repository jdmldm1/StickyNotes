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

        // Graph model
        private readonly List<GraphNode> _nodes = new();
        private readonly List<GraphEdge> _edges = new();

        // Canvas interaction
        private Point _panStart;
        private bool _isPanning;
        private string? _highlightedTag;

        // Node colors mapped from note color names
        private static readonly Dictionary<string, Color> NoteColorMap = new()
        {
            { "yellow",  Color.FromRgb(0xD4, 0x9A, 0x13) },
            { "green",   Color.FromRgb(0x1A, 0x8F, 0x54) },
            { "pink",    Color.FromRgb(0xC2, 0x18, 0x5B) },
            { "purple",  Color.FromRgb(0x7B, 0x1F, 0xA2) },
            { "blue",    Color.FromRgb(0x02, 0x88, 0xD1) },
            { "charcoal",Color.FromRgb(0x42, 0x42, 0x42) },
        };

        private class GraphNode
        {
            public int NoteId;
            public string Title = "";
            public string ColorName = "yellow";
            public double X, Y;
            public Ellipse? Shape;
            public TextBlock? Label;
        }

        private class GraphEdge
        {
            public GraphNode A = null!;
            public GraphNode B = null!;
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
            BuildGraph();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Nothing to flush
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // ── Graph build ────────────────────────────────────────────────────────

        private void BuildGraph()
        {
            _nodes.Clear();
            _edges.Clear();
            GraphCanvas.Children.Clear();
            TagLegendPanel.Children.Clear();

            var pairs = DatabaseHelper.GetAllNoteTagPairs();

            if (pairs.Count == 0)
            {
                StatusText.Text = "No tags found. Add tags to your notes to see connections.";
                return;
            }

            // Build per-note tag sets
            var noteInfo = new Dictionary<int, (string Title, string Color)>();
            var noteTags = new Dictionary<int, HashSet<string>>();
            foreach (var (noteId, title, color, tag) in pairs)
            {
                noteInfo[noteId] = (title, color);
                if (!noteTags.ContainsKey(noteId)) noteTags[noteId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                noteTags[noteId].Add(tag);
            }

            // Create nodes (only notes with ≥1 tag)
            var rand = new Random(42);
            double cx = GraphCanvasBorder.ActualWidth > 0 ? GraphCanvasBorder.ActualWidth / 2 : 400;
            double cy = GraphCanvasBorder.ActualHeight > 0 ? GraphCanvasBorder.ActualHeight / 2 : 300;
            foreach (var (noteId, (title, color)) in noteInfo)
            {
                _nodes.Add(new GraphNode
                {
                    NoteId = noteId,
                    Title = string.IsNullOrEmpty(title) ? $"Note {noteId}" : title,
                    ColorName = color,
                    X = cx + (rand.NextDouble() - 0.5) * 400,
                    Y = cy + (rand.NextDouble() - 0.5) * 300,
                });
            }

            // Create edges for pairs sharing ≥1 tag
            var nodeById = _nodes.ToDictionary(n => n.NoteId);
            for (int i = 0; i < _nodes.Count; i++)
            for (int j = i + 1; j < _nodes.Count; j++)
            {
                var a = _nodes[i];
                var b = _nodes[j];
                if (!noteTags.ContainsKey(a.NoteId) || !noteTags.ContainsKey(b.NoteId)) continue;
                var shared = noteTags[a.NoteId].Intersect(noteTags[b.NoteId]).OrderBy(t => t).ToList();
                if (shared.Count > 0)
                    _edges.Add(new GraphEdge { A = a, B = b, SharedTags = shared });
            }

            // Collect all tags for legend
            var allTags = noteTags.Values.SelectMany(s => s).Distinct().OrderBy(t => t).ToList();
            BuildTagLegend(allTags);

            double width = GraphCanvasBorder.ActualWidth > 0 ? GraphCanvasBorder.ActualWidth : 960;
            double height = GraphCanvasBorder.ActualHeight > 0 ? GraphCanvasBorder.ActualHeight : 560;

            // Assign initial positions via force-directed layout (run async so window renders first)
            Task.Run(() => RunForceLayout(width, height)).ContinueWith(_ =>
                Dispatcher.Invoke(() => RenderGraph()), TaskScheduler.Default);

            StatusText.Text = $"{_nodes.Count} notes · {_edges.Count} connections · {allTags.Count} tags";
        }

        private void BuildTagLegend(List<string> tags)
        {
            TagLegendPanel.Children.Clear();
            foreach (var tag in tags)
            {
                var pill = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(60, 0, 132, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 132, 255)),
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
                UpdateEdgeHighlights();
            }
        }

        private void UpdateEdgeHighlights()
        {
            foreach (var edge in _edges)
            {
                if (edge.Line == null) continue;
                bool highlighted = _highlightedTag == null || edge.SharedTags.Contains(_highlightedTag, StringComparer.OrdinalIgnoreCase);
                edge.Line.Opacity = highlighted ? 1.0 : 0.15;
                if (edge.TagLabel != null) edge.TagLabel.Opacity = highlighted ? 1.0 : 0.1;
            }
        }

        // ── Force-directed layout (Fruchterman-Reingold) ─────────────────────

        private void RunForceLayout(double width, double height)
        {
            if (_nodes.Count == 0) return;

            const int iterations = 100;
            double k = Math.Max(140.0, Math.Sqrt((width * height) / _nodes.Count) * 0.9); // Dynamic spring length
            double temp = width / 8.0;
            double cooling = temp / (iterations + 1);

            for (int iter = 0; iter < iterations; iter++)
            {
                // Repulsion
                var disps = new (double dx, double dy)[_nodes.Count];
                for (int i = 0; i < _nodes.Count; i++)
                for (int j = 0; j < _nodes.Count; j++)
                {
                    if (i == j) continue;
                    double dx = _nodes[i].X - _nodes[j].X;
                    double dy = _nodes[i].Y - _nodes[j].Y;
                    double dist = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy));
                    double force = (k * k) / dist;
                    if (dist < 120) force *= 1.8; // Give an extra repulsive push to nearby nodes to prevent label overlap
                    disps[i].dx += (dx / dist) * force;
                    disps[i].dy += (dy / dist) * force;
                }

                // Attraction along edges
                foreach (var edge in _edges)
                {
                    int ai = _nodes.IndexOf(edge.A);
                    int bi = _nodes.IndexOf(edge.B);
                    double dx = edge.A.X - edge.B.X;
                    double dy = edge.A.Y - edge.B.Y;
                    double dist = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy));
                    double force = (dist * dist) / k;
                    double fx = (dx / dist) * force;
                    double fy = (dy / dist) * force;
                    disps[ai].dx -= fx;
                    disps[ai].dy -= fy;
                    disps[bi].dx += fx;
                    disps[bi].dy += fy;
                }

                // Gravity toward center
                for (int i = 0; i < _nodes.Count; i++)
                {
                    double dx = _nodes[i].X - width / 2;
                    double dy = _nodes[i].Y - height / 2;
                    disps[i].dx -= dx * 0.015;
                    disps[i].dy -= dy * 0.015;
                }

                // Apply displacements with temperature cap
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

        // ── Render ─────────────────────────────────────────────────────────────

        private void RenderGraph()
        {
            if (GraphCanvas == null || ShowLabelsCheck == null) return;
            GraphCanvas.Children.Clear();
            bool showLabels = ShowLabelsCheck.IsChecked == true;

            // Draw edges first (behind nodes)
            foreach (var edge in _edges)
            {
                var line = new Line
                {
                    X1 = edge.A.X, Y1 = edge.A.Y,
                    X2 = edge.B.X, Y2 = edge.B.Y,
                    Stroke = new SolidColorBrush(Color.FromArgb(120, 0, 150, 255)),
                    StrokeThickness = 1.5,
                };
                edge.Line = line;
                GraphCanvas.Children.Add(line);

                if (showLabels && edge.SharedTags.Count > 0)
                {
                    string labelText = edge.SharedTags.Count <= 2
                        ? string.Join(", ", edge.SharedTags.Select(t => "#" + t))
                        : $"{edge.SharedTags.Count} tags";

                    var edgeLabel = new TextBlock
                    {
                        Text = labelText,
                        Foreground = new SolidColorBrush(Color.FromArgb(140, 100, 180, 255)),
                        FontSize = 9,
                    };
                    edge.TagLabel = edgeLabel;
                    Canvas.SetLeft(edgeLabel, (edge.A.X + edge.B.X) / 2);
                    Canvas.SetTop(edgeLabel, (edge.A.Y + edge.B.Y) / 2);
                    GraphCanvas.Children.Add(edgeLabel);
                }
            }

            // Draw nodes
            const double nodeRadius = 22;
            foreach (var node in _nodes)
            {
                Color nodeColor = NoteColorMap.TryGetValue(node.ColorName, out Color c) ? c : NoteColorMap["yellow"];

                var circle = new Ellipse
                {
                    Width = nodeRadius * 2,
                    Height = nodeRadius * 2,
                    Fill = new RadialGradientBrush(
                        Color.FromArgb(220, nodeColor.R, nodeColor.G, nodeColor.B),
                        Color.FromArgb(120, (byte)(nodeColor.R / 2), (byte)(nodeColor.G / 2), (byte)(nodeColor.B / 2))),
                    Stroke = new SolidColorBrush(Color.FromArgb(200, nodeColor.R, nodeColor.G, nodeColor.B)),
                    StrokeThickness = 2,
                    Cursor = Cursors.Hand,
                    ToolTip = node.Title,
                    Tag = node,
                };
                circle.MouseLeftButtonDown += Node_MouseLeftButtonDown;
                circle.MouseLeftButtonUp += Node_MouseLeftButtonUp;
                circle.MouseMove += Node_MouseMove;
                node.Shape = circle;

                Canvas.SetLeft(circle, node.X - nodeRadius);
                Canvas.SetTop(circle, node.Y - nodeRadius);
                GraphCanvas.Children.Add(circle);

                if (showLabels)
                {
                    var label = new TextBlock
                    {
                        Text = node.Title.Length > 14 ? node.Title.Substring(0, 12) + "…" : node.Title,
                        Foreground = Brushes.White,
                        FontSize = 10,
                        TextAlignment = TextAlignment.Center,
                        IsHitTestVisible = false,
                    };
                    node.Label = label;
                    Canvas.SetLeft(label, node.X - 35);
                    Canvas.SetTop(label, node.Y + nodeRadius + 3);
                    GraphCanvas.Children.Add(label);
                }
            }
        }

        // ── Node drag ──────────────────────────────────────────────────────────

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
                    // It was a click — open the note
                    _mainWnd.OpenNoteWindow(_draggingNode.NoteId);
                }
                _draggingNode = null;
            }
        }

        private void UpdateNodePosition(GraphNode node)
        {
            const double r = 22;
            if (node.Shape != null)
            {
                Canvas.SetLeft(node.Shape, node.X - r);
                Canvas.SetTop(node.Shape, node.Y - r);
            }
            if (node.Label != null)
            {
                Canvas.SetLeft(node.Label, node.X - 35);
                Canvas.SetTop(node.Label, node.Y + r + 3);
            }
            // Update connected edges
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

        // ── Canvas pan & zoom ──────────────────────────────────────────────────

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

        // ── Controls ───────────────────────────────────────────────────────────

        private void ResetLayout_Click(object sender, RoutedEventArgs e)
        {
            GraphScale.ScaleX = GraphScale.ScaleY = 1;
            GraphTranslate.X = GraphTranslate.Y = 0;
            double width = GraphCanvasBorder.ActualWidth > 0 ? GraphCanvasBorder.ActualWidth : 960;
            double height = GraphCanvasBorder.ActualHeight > 0 ? GraphCanvasBorder.ActualHeight : 560;
            Task.Run(() => RunForceLayout(width, height)).ContinueWith(_ =>
                Dispatcher.Invoke(() => RenderGraph()), TaskScheduler.Default);
        }

        private void ShowLabels_Changed(object sender, RoutedEventArgs e) => RenderGraph();
    }
}
