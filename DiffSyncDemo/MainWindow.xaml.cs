﻿using System;
using System.Collections.Generic;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using DiffSync;
using DiffSync.NET;

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
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }



        ProtocolStateMachine<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>> server = new DiffSync.NET.ProtocolStateMachine<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>>();
        ProtocolStateMachine<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>> client = new DiffSync.NET.ProtocolStateMachine<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>>();
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            server.Initialize(new ExampleDiffSyncable(), new ExampleDiffSyncable(), new ExampleDiffSyncable());
            client.Initialize(new ExampleDiffSyncable(), new ExampleDiffSyncable(), new ExampleDiffSyncable());
            RefreshUI();
        }

        JsonInkConverter j = new JsonInkConverter();
        private void RefreshUI()
        {
            var prevServerLive = stateServerLive.DataContext as State<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>>;
            var prevServerShadow = stateServerShadow.DataContext as ShadowState<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>>;
            var prevServerBackupShadow = stateServerBackupShadow.DataContext as BackupShadowState<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>>;
            var prevClientLive = stateClientLive.DataContext as State<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>>;
            var prevClientShadow = stateClientShadow.DataContext as ShadowState<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>>;
            var prevClientBackupShadow = stateClientBackupShadow.DataContext as BackupShadowState<ExampleDiffSyncable, ExampleDiff, Dictionary<string, object>>;

            txtSerializedStateClient.Text = Newtonsoft.Json.JsonConvert.SerializeObject(client);
            txtSerializedStateServer.Text = Newtonsoft.Json.JsonConvert.SerializeObject(server,j);

            //if (prevServerLive == null || prevServerLive.Version != server.Live.Version)
            {
                stateServerLive.DataContext = null;
                stateServerLive.DataContext = server.Live;
            }

            //if (prevServerShadow == null || prevServerShadow.Version != server.Shadow.Version || prevServerShadow.PeerVersion != server.Shadow.PeerVersion)
            {
                stateServerShadow.DataContext = null;
                stateServerShadow.DataContext = server.Shadow;
            }

            //if (prevServerBackupShadow == null || prevServerBackupShadow.Version != server.BackupShadow.Version || prevServerBackupShadow.PeerVersion != server.Shadow.PeerVersion)
            {
                stateServerBackupShadow.DataContext = null;
                stateServerBackupShadow.DataContext = server.BackupShadow;
            }

            //if (prevClientLive == null || prevClientLive.Version != client.Live.Version)
            {
                stateClientLive.DataContext = null;
                stateClientLive.DataContext = client.Live;
            }

            //if (prevClientShadow == null || prevClientShadow.Version != client.Shadow.Version || prevClientShadow.PeerVersion != client.Shadow.PeerVersion)
            {
                stateClientShadow.DataContext = null;
                stateClientShadow.DataContext = client.Shadow;
            }

            //if (prevClientBackupShadow == null || prevClientBackupShadow.Version != client.BackupShadow.Version || prevClientBackupShadow.PeerVersion != client.BackupShadow.PeerVersion)
            {
                stateClientBackupShadow.DataContext = null;
                stateClientBackupShadow.DataContext = client.BackupShadow;
            }




            diffFromServer.DataContext = null;
            diffFromClient.DataContext = null;

            patchFromClientLive.DataContext = null;
            patchFromClientShadow.DataContext = null;
            patchFromServerLive.DataContext = null;
            patchFromServerShadow.DataContext = null;
            diffFromServer.DataContext = serverMessageSent;
            diffFromClient.DataContext = clientMessageSent;

            patchFromClientLive.DataContext = clientMessage;
            patchFromClientShadow.DataContext = clientMessage;
            patchFromServerLive.DataContext = serverMessage;
            patchFromServerShadow.DataContext = serverMessage;
        }

        Message<ExampleDiff> serverMessage, serverMessageSent, clientMessage, clientMessageSent;

        private void BtnClientReadServerMsg_Click(object sender, RoutedEventArgs e)
        {
            if (serverMessage != null)
            {
                if (!client.TryReceiveEdits(serverMessage))
                    serverMessage = null;

                RefreshUI();
            }
        }

        private void BtnClientProcessLocal_Click(object sender, RoutedEventArgs e)
        {
            client.DiffApplyLive();
            client.DiffApplyShadow();

            RefreshUI();

        }

        private List<ExampleDiff> appliedClientEdits = new List<ExampleDiff>();
        private void BtnClientProcessServerMsg_Click(object sender, RoutedEventArgs e)
        {
            appliedClientEdits = client.ProcessEditsToShadow(serverMessage);

            RefreshUI();
        }


        private void BtnClientProcessServerLiveMsg_Click(object sender, RoutedEventArgs e)
        {
            client.ProcessEditsToLive(appliedClientEdits, true);

            RefreshUI();
        }

        private void BtnClientGenerateMsg_Click(object sender, RoutedEventArgs e)
        {
            clientMessageSent = client.GenerateMessage(serverMessage);
            serverMessage = null;
            RefreshUI();
        }

        private void BtnServerReadClientMsg_Click(object sender, RoutedEventArgs e)
        {
            if (clientMessage != null)
            {
                if (!server.TryReceiveEdits(clientMessage))
                    clientMessage = null;

                RefreshUI();
            }
        }

        private void BtnServerProcessLocal_Click(object sender, RoutedEventArgs e)
        {
            client.DiffApplyLive();
            server.DiffApplyShadow();

            RefreshUI();
        }

        private List<ExampleDiff> appliedServerEdits = new List<ExampleDiff>();
        private void BtnServerProcessClientMsg_Click(object sender, RoutedEventArgs e)
        {
            appliedServerEdits = server.ProcessEditsToShadow(clientMessage);

            RefreshUI();
        }

        private void BtnServerProcessClientLiveMsg_Click(object sender, RoutedEventArgs e)
        {
            server.ProcessEditsToLive(appliedServerEdits, false);

            RefreshUI();
        }

        private void BtnClientRevertToBackupCheck_Click(object sender, RoutedEventArgs e)
        {
            client.CheckAndPerformBackupRevert(serverMessage);
            RefreshUI();
        }

        private void BtnClientTakeBackupCheck_Click(object sender, RoutedEventArgs e)
        {
            client.TakeBackupIfApplicable(serverMessage);
            RefreshUI();
        }

        private void BtnServerRevertToBackupCheck_Click(object sender, RoutedEventArgs e)
        {
            server.CheckAndPerformBackupRevert(clientMessage);
            RefreshUI();
        }

        private void BtnServerTakeBackupCheck_Click(object sender, RoutedEventArgs e)
        {
            server.TakeBackupIfApplicable(clientMessage);
            RefreshUI();
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            int bigDelay = 500;
            int smallDelay = 50;
            while(false) {
                await Task.Delay(bigDelay);
                BtnClientReadServerMsg_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnClientRevertToBackupCheck_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnClientProcessServerMsg_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnClientTakeBackupCheck_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnClientProcessServerLiveMsg_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnClientProcessLocal_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnClientGenerateMsg_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnClientTransmitMsg_Click(sender, e);
                await Task.Delay(bigDelay);

                if (serializeDeserializeServerState)
                {
                    server = Newtonsoft.Json.JsonConvert.DeserializeObject< ProtocolStateMachine < ExampleDiffSyncable, ExampleDiff, Dictionary<string, object> >>( txtSerializedStateServer.Text, j);
                    var serializedMessage = Newtonsoft.Json.JsonConvert.SerializeObject(clientMessage, j);

                    clientMessage = Newtonsoft.Json.JsonConvert.DeserializeObject<Message<ExampleDiff>>(serializedMessage, j);
                }

                BtnServerReadClientMsg_Click (sender, e);
                await Task.Delay(smallDelay);
                BtnServerRevertToBackupCheck_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnServerProcessClientMsg_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnServerTakeBackupCheck_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnServerProcessClientLiveMsg_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnServerProcessLocal_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnServerGenerateMsg_Click(sender, e);
                await Task.Delay(smallDelay);
                BtnServerTransmitMsg_Click(sender, e);
            }
        }

        private void SldTextChange_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }

        private void SldPacketDupe_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }

        private void SldPacketLoss_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }

        private void SldSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {

        }

        private bool serializeDeserializeServerState = false;
        private void ChkSerializeDeserializeServerState_Checked(object sender, RoutedEventArgs e)
        {
            serializeDeserializeServerState = chkSerializeDeserializeServerState.IsChecked ?? false;
        }

        private void ChkSerializeDeserializeServerState_Unchecked(object sender, RoutedEventArgs e)
        {
            serializeDeserializeServerState = chkSerializeDeserializeServerState.IsChecked ?? false;
        }

        private void BtnServerGenerateMsg_Click(object sender, RoutedEventArgs e)
        {
            serverMessageSent = server.GenerateMessage(clientMessage);

            clientMessage = null;

            RefreshUI();
        }

        private void BtnClientTransmitMsg_Click(object sender, RoutedEventArgs e)
        {
            clientMessage = clientMessageSent;

            clientMessageSent = null;

            RefreshUI();
        }

        private void BtnServerTransmitMsg_Click(object sender, RoutedEventArgs e)
        {
            serverMessage = serverMessageSent;

            serverMessageSent = null;

            RefreshUI();
        }

    }
}
