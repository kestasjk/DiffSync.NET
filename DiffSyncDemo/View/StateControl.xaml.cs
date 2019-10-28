using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

/*
    DiffSync.NET - A Differential Synchronization library for .NET
    Copyright (C) 2019 Kestas J. Kuliukas

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2.1 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301
    USA
    */

namespace DiffSyncDemo.View
{
    /// <summary>
    /// Interaction logic for StateControl.xaml
    /// </summary>
    public partial class StateControl : UserControl
    {
        public StateControl()
        {
            InitializeComponent();
        }

        public string Header { get; set; }

        private void InkCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            
            using (var ms = new MemoryStream())
            {
               ink_.Strokes.Save(ms);
                ms.Position = 0;
                var res = ms.ToArray();
                var state = DataContext as DiffSync.NET.State<ExampleDiffSyncable,ExampleDiff, IReadOnlyDictionary<string,object>>;
                state.StateObject.Ink = res;

            }

        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            SetInk();
        }
        private void SetInk()
        {
            var state = DataContext as DiffSync.NET.State<ExampleDiffSyncable, ExampleDiff, IReadOnlyDictionary<string, object>>;
            if (state?.StateObject?.Ink != null)
            {
                using (var ms = new MemoryStream(state.StateObject.Ink))
                {
                    var sc = new StrokeCollection(ms);
                    ink_.Strokes = sc;
                }
            }
            else
            {
                ink_.Strokes = new StrokeCollection(); ;
            }
        }

        private void UserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {

            SetInk();
        }

        private void BtnClearInk_Click(object sender, RoutedEventArgs e)
        {

            ink_.Strokes.Clear();
            var state = DataContext as DiffSync.NET.State<ExampleDiffSyncable, ExampleDiff, IReadOnlyDictionary<string, object>>;
            if (state?.StateObject?.Ink != null)
            {
                state.StateObject.Ink = null;
            }
        }
    }
}
