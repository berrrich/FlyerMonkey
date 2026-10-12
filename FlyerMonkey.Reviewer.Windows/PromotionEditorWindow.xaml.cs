
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FlyerMonkey.Reviewer.Windows
{
    public partial class PromotionEditorWindow : Window
    {
        public class PromotionChoice
        {
            public string Name { get; set; } = "";
            public bool IsSelected { get; set; }
        }

        private readonly List<PromotionChoice> _choices;

        public List<string> SelectedPromotionTypes { get; private set; }
            = new();

        public PromotionEditorWindow(
            string productName,
            string? originalPromotion,
            IEnumerable<string> availableTypes,
            IEnumerable<string>? selectedTypes)
        {
            InitializeComponent();

            ProductNameText.Text = productName;

            OriginalPromotionText.Text =
                string.IsNullOrWhiteSpace(originalPromotion)
                    ? "(No promotion text)"
                    : originalPromotion;

            var existingSelections = new HashSet<string>(
                selectedTypes ?? Enumerable.Empty<string>(),
                System.StringComparer.OrdinalIgnoreCase);

            _choices = availableTypes
                .Select(name => new PromotionChoice
                {
                    Name = name,
                    IsSelected = existingSelections.Contains(name)
                })
                .ToList();

            PromotionTypesList.ItemsSource = _choices;
        }

        private void SavePromotionTypes_Click(
            object sender,
            RoutedEventArgs e)
        {
            SelectedPromotionTypes = _choices
                .Where(c => c.IsSelected)
                .Select(c => c.Name)
                .ToList();

            DialogResult = true;
        }
    }
}
