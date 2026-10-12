using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FlyerMonkey.Reviewer.Windows
{
    public partial class CategoryEditorWindow : Window
    {
        public class CategoryChoice
        {
            public string Name { get; set; } = "";
            public bool IsSelected { get; set; }
        }

        private readonly List<CategoryChoice> _choices;

        public string? PrimaryCategory { get; private set; }

        public List<string> SelectedCategories { get; private set; }
            = new();

        public CategoryEditorWindow(
            string productName,
            IEnumerable<string> availableCategories,
            string? primaryCategory,
            IEnumerable<string>? selectedCategories)
        {
            InitializeComponent();

            ProductNameText.Text = productName;

            _choices = availableCategories
                .Select(name => new CategoryChoice
                {
                    Name = name,
                    IsSelected =
                        selectedCategories?.Contains(name) == true
                })
                .ToList();

            PrimaryCategoryCombo.ItemsSource = _choices;
            AdditionalCategoriesList.ItemsSource = _choices;

            PrimaryCategoryCombo.SelectedItem =
                _choices.FirstOrDefault(
                    c => c.Name == primaryCategory);
        }

        private void SaveCategories_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (PrimaryCategoryCombo.SelectedItem
                is not CategoryChoice primary)
            {
                MessageBox.Show(
                    "Please select a primary category.");
                return;
            }

            PrimaryCategory = primary.Name;

            SelectedCategories = _choices
                .Where(c => c.IsSelected)
                .Select(c => c.Name)
                .ToList();

            if (!SelectedCategories.Contains(PrimaryCategory))
            {
                SelectedCategories.Insert(0, PrimaryCategory);
            }

            DialogResult = true;
        }
    }
}