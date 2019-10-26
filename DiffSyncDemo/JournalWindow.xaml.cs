using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

namespace DiffSyncDemo
{
    /// <summary>
    /// Interaction logic for JournalWindow.xaml
    /// </summary>
    public partial class JournalWindow : Window
    {
        public JournalWindow()
        {
            InitializeComponent();
        }
        ObservableCollection<DiffSync.NET.StateDataDictionary> Client = new ObservableCollection<DiffSync.NET.StateDataDictionary>();
        ObservableCollection<DiffSync.NET.StateDataDictionary> Server = new ObservableCollection<DiffSync.NET.StateDataDictionary>();
        public void AddToServer(DiffSync.NET.StateDataDictionary dict)
        {
            Server.Add(dict);
            serverView?.Refresh();
        }
        public void AddToClient(DiffSync.NET.StateDataDictionary dict)
        {
            Client.Add(dict);
            clientView?.Refresh();
        }
        ICollectionView clientView, serverView;
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            clientView = CollectionViewSource.GetDefaultView(Client);
            serverView = CollectionViewSource.GetDefaultView(Server);
            dgdClient.ItemsSource = clientView;
            dgdServer.ItemsSource = serverView;
        }
    }
}
