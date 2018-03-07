using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using ListView = System.Windows.Controls.ListView;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace labeltool
{
    /// <inheritdoc>
    ///     <cref></cref>
    /// </inheritdoc>
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private class MyLabelData
        {
            public string MyUrl { get; set; }
            public string MyMultipolygon { get; set; }
            public List<string> RawPolygons { get; set; }
        }

        private List<MyLabelData> _myLabels;

        public MainWindow()
        {
            InitializeComponent();
        }
        

        private void ImportJson(object sender, RoutedEventArgs e)
        {
            OpenFileDialog myFileDialog = new OpenFileDialog
            {
                Filter = "JSON schema|*.json",
                Title = "Load JSON SCHEMA"
            };
            if (myFileDialog.ShowDialog() != true)
            {
                return;
            }

            string myJsonSchema = File.ReadAllText(myFileDialog.FileName);

            OpenFileDialog myFileDialog2 = new OpenFileDialog
            {
                Filter = "Labelbox JSON Export|*.json",
                Title = "Import an exported JSON from labelbox.io"
            };
            if (myFileDialog2.ShowDialog() != true)
            {
                return;
            }

            string myJson = File.ReadAllText(myFileDialog2.FileName);

            JSchema schema = JSchema.Parse(myJsonSchema);
            dynamic myLabels = JsonConvert.DeserializeObject(myJson);

            JToken json = JToken.Parse(myJson);

            bool isValid = json.IsValid(schema);
            if (!isValid)
            {
               return;
            }

            _myLabels = new List<MyLabelData>();
            foreach (dynamic myLabel in myLabels)
            {
                string thisUrl = myLabel["Labeled Data"].ToString();
                string curLabel = null;
                List<string> rawLabels = new List<string>();
                foreach (dynamic multilabel in myLabel.Label)
                {
                    string labeltitle = multilabel.Name.ToString();

                    string thisMultipolygon = multilabel.Value.ToString();
                    string thisPolygon = thisMultipolygon.Replace("MULTIPOLYGON (", "");
                    thisPolygon = thisPolygon.Replace("((", "");
                    thisPolygon = thisPolygon.Replace(")), ", ";");
                    thisPolygon = thisPolygon.Replace(")", "");
                    string[] polyArray = thisPolygon.Split(';');
                    foreach (string thisPoly in polyArray)
                    {
                        if (curLabel != null)
                        {
                            curLabel += Environment.NewLine;
                        }
                        curLabel += labeltitle + " " + thisPoly.Replace(",", "");
                        rawLabels.Add(thisPoly.Replace(", ",","));
                    }
                }

                _myLabels.Add(new MyLabelData
                {
                    MyUrl = thisUrl,
                    MyMultipolygon = curLabel,
                    RawPolygons = rawLabels
                });
            }

            LabelList.ItemsSource = _myLabels;
        }

        private void CreateFolderStructure(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog myDiag = new FolderBrowserDialog();
            DialogResult result = myDiag.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK) return;
            if (_myLabels == null) return;

            string folderName = myDiag.SelectedPath;
            Directory.CreateDirectory(folderName + "\\images");
            Directory.CreateDirectory(folderName + "\\labels");

            foreach (MyLabelData label in _myLabels)
            {
                string[] urlSplit = label.MyMultipolygon.Split('/');
                string myFileName = urlSplit[urlSplit.Length - 1];
                string[] fileNameSplit = myFileName.Split('.');
                File.WriteAllText(folderName + "\\labels\\" + fileNameSplit[0] + ".txt", label.MyMultipolygon);
                
                using (var client = new WebClient())
                {
                    client.DownloadFile(label.MyUrl, folderName + "\\images\\" + myFileName);
                }
            }
        }

        private void LabelList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MyLabelData selectedLabel = (MyLabelData)((ListView)sender).SelectedItem;
            if (selectedLabel == null) return;

            Pictureviewer pictureviewer = new Pictureviewer(selectedLabel.MyUrl, selectedLabel.RawPolygons);
            pictureviewer.Show();
        }
    }
}
