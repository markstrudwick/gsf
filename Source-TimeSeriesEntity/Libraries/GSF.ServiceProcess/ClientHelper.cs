﻿//******************************************************************************************************
//  ClientHelper.cs - Gbtc
//
//  Copyright © 2012, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the Eclipse Public License -v 1.0 (the "License"); you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/eclipse-1.0.php
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  08/29/2006 - Pinal C. Patel
//       Original version of source code generated.
//  11/30/2007 - Pinal C. Patel
//       Modified the "design time" check in EndInit() method to use LicenseManager.UsageMode property
//       instead of DesignMode property as the former is more accurate than the latter.
//  09/30/2008 - J. Ritchie Carroll
//       Convert to C#.
//  07/15/2009 - Pinal C. Patel
//       Added AuthenticationMethod, AuthenticationUsername and AuthenticationPassword properties to
//       provision for the authentication process as part of security.
//       Added ISupportLifecycle, ISupportInitialize and IPersistSettings interface implementations
//       to support the persistence and retrieval of settings from the config file.
//  07/17/2009 - Pinal C. Patel
//       Added static PretendRequest() method that can be used to create pretend request for manually
//       invoking request handlers registered with the ServiceHelper.
//  07/21/2009 - Pinal C. Patel
//       Replace AuthenticationUsername and AuthenticationPassword properties with AuthenticationInput
//       to allow for input text to be specified for any AuthenticationMethod instead of just Ntml.
//  07/23/2009 - Pinal C. Patel
//       ReceivedServiceResponse is now raised only for custom service responses instead of all.
//  09/14/2009 - Stephen C. Wills
//       Added new header and license agreement.
//  10/23/2009 - Pinal C. Patel
//       Modified ReceivedServiceUpdate event to support the change in ServiceHelper.UpdateStatus().
//  06/16/2010 - Pinal C. Patel
//       Made changes necessary to implement role-based security.
//  04/14/2011 - Pinal C. Patel
//       Updated to use new deserialization methods in GSF.Serialization class.
//  12/20/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.Text;
using System.Threading;
using GSF.Communication;
using GSF.Configuration;

namespace GSF.ServiceProcess
{
    /// <summary>
    /// Component that provides client-side functionality to <see cref="ServiceHelper"/>.
    /// </summary>
    [ToolboxBitmap(typeof(ClientHelper))]
    public class ClientHelper : Component, ISupportLifecycle, ISupportInitialize, IPersistSettings
    {
        #region [ Members ]

        // Constants

        /// <summary>
        /// Specifies the default value for the <see cref="Username"/> property.
        /// </summary>
        public const string DefaultUsername = "";

        /// <summary>
        /// Specifies the default value for the <see cref="Password"/> property.
        /// </summary>
        public const string DefaultPassword = "";

        /// <summary>
        /// Specifies the default value for the <see cref="PersistSettings"/> property.
        /// </summary>
        public const bool DefaultPersistSettings = false;

        /// <summary>
        /// Specifies the default value for the <see cref="SettingsCategory"/> property.
        /// </summary>
        public const string DefaultSettingsCategory = "ClientHelper";

        // Events

        /// <summary>
        /// Occurs when a status update is received from the <see cref="ServiceHelper"/>.
        /// </summary>
        [Category("Client"),
        Description("Occurs when a status update is received from the ServiceHelper.")]
        public event EventHandler<EventArgs<UpdateType, string>> ReceivedServiceUpdate;

        /// <summary>
        /// Occurs when a custom <see cref="ServiceResponse"/> is received from the <see cref="ServiceHelper"/>.
        /// </summary>
        [Category("Service"),
        Description("Occurs when a ServiceResponse is received from the ServiceHelper.")]
        public event EventHandler<EventArgs<ServiceResponse>> ReceivedServiceResponse;

        /// <summary>
        /// Occurs when the state of the <see cref="ServiceHelper"/> is changed.
        /// </summary>
        [Category("Service"),
        Description("Occurs when the state of the ServiceHelper is changed.")]
        public event EventHandler<EventArgs<ObjectState<ServiceState>>> ServiceStateChanged;

        /// <summary>
        /// Occurs when the state of a <see cref="ServiceProcess"/> is changed.
        /// </summary>
        [Category("Service"),
        Description("Occurs when the state of a ServiceProcess is changed.")]
        public event EventHandler<EventArgs<ObjectState<ServiceProcessState>>> ProcessStateChanged;

        /// <summary>
        /// Occurs when the <see cref="ServiceHelper"/> successfully authenticates the <see cref="ClientHelper"/>.
        /// </summary>
        [Category("Security"),
        Description("Occurs when the ServiceHelper successfully authenticates the ClientHelper.")]
        public event EventHandler AuthenticationSuccess;

        /// <summary>
        /// Occurs when the <see cref="ServiceHelper"/> fails to authenticate the <see cref="ClientHelper"/>.
        /// </summary>
        /// <remarks>
        /// Set <see cref="CancelEventArgs.Cancel"/> to <b>true</b> to continue with connection attempts even after authentication fails. 
        /// This can be useful for re-authenticating the <see cref="ClientHelper"/> using different <see cref="Username"/> and <see cref="Password"/>.
        /// </remarks>
        [Category("Security"),
        Description("Occurs when the ServiceHelper fails to authenticate the ClientHelper.")]
        public event EventHandler<CancelEventArgs> AuthenticationFailure;

        /// <summary>
        /// Occurs when a telnet session has been established.
        /// </summary>
        [Category("Command"),
        Description("Occurs when a telnet session has been established.")]
        public event EventHandler TelnetSessionEstablished;

        /// <summary>
        /// Occurs when a telnet session has been terminated.
        /// </summary>
        [Category("Command"),
        Description("Occurs when a telnet session has been terminated.")]
        public event EventHandler TelnetSessionTerminated;

        // Fields
        private ClientBase m_remotingClient;
        private string m_username;
        private string m_password;
        private bool m_persistSettings;
        private string m_settingsCategory;
        private bool m_attemptReconnection;
        private bool m_authenticationComplete;
        private bool m_disposed;
        private bool m_initialized;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientHelper"/> class.
        /// </summary>
        public ClientHelper()
        {
            m_username = DefaultUsername;
            m_password = DefaultPassword;
            m_persistSettings = DefaultPersistSettings;
            m_settingsCategory = DefaultSettingsCategory;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientHelper"/> class.
        /// </summary>
        /// <param name="container"><see cref="IContainer"/> object that contains the <see cref="ClientHelper"/>.</param>
        public ClientHelper(IContainer container)
            : this()
        {
            if (container != null)
                container.Add(this);
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets the <see cref="ClientBase"/> object used for communicating with the <see cref="ServiceHelper"/>.
        /// </summary>
        [Category("Components"),
        Description("ClientBase object used for communicating with the ServiceHelper.")]
        public ClientBase RemotingClient
        {
            get
            {
                return m_remotingClient;
            }
            set
            {
                if (m_remotingClient != null)
                {
                    // Detach events from any existing instance
                    m_remotingClient.ConnectionEstablished -= RemotingClient_ConnectionEstablished;
                    m_remotingClient.ConnectionAttempt -= RemotingClient_ConnectionAttempt;
                    m_remotingClient.ConnectionException -= RemotingClient_ConnectionException;
                    m_remotingClient.ConnectionTerminated -= RemotingClient_ConnectionTerminated;
                    m_remotingClient.ReceiveDataComplete -= RemotingClient_ReceiveDataComplete;
                }

                m_remotingClient = value;

                if (m_remotingClient != null)
                {
                    // Attach events to new instance
                    m_remotingClient.ConnectionEstablished += RemotingClient_ConnectionEstablished;
                    m_remotingClient.ConnectionAttempt += RemotingClient_ConnectionAttempt;
                    m_remotingClient.ConnectionException += RemotingClient_ConnectionException;
                    m_remotingClient.ConnectionTerminated += RemotingClient_ConnectionTerminated;
                    m_remotingClient.ReceiveDataComplete += RemotingClient_ReceiveDataComplete;
                }
            }
        }

        /// <summary>
        /// Gets or sets the username of the <see cref="ClientHelper"/>'s user to be used for authenticating with the <see cref="ServiceHelper"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value being specified is a null string.</exception>
        [Category("Security"),
        DefaultValue(DefaultUsername),
        Description("Username of the ClientHelper's user to be used for authenticating with the ServiceHelper.")]
        public string Username
        {
            get
            {
                return m_username;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value");

                m_username = value;
            }
        }

        /// <summary>
        /// Gets or sets the password of the <see cref="ClientHelper"/>'s user to be used for authenticating with the <see cref="ServiceHelper"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value being specified is a null string.</exception>
        [Category("Security"),
        DefaultValue(DefaultPassword),
        Description("Password of the ClientHelper's user to be used for authenticating with the ServiceHelper.")]
        public string Password
        {
            get
            {
                return m_password;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value");

                m_password = value;
            }
        }

        /// <summary>
        /// Gets or sets a boolean value that indicates whether the settings of <see cref="ClientHelper"/> are to be saved to the config file.
        /// </summary>
        [Category("Persistence"),
        DefaultValue(DefaultPersistSettings),
        Description("Indicates whether the settings of ClientHelper are to be saved to the config file.")]
        public bool PersistSettings
        {
            get
            {
                return m_persistSettings;
            }
            set
            {
                m_persistSettings = value;
            }
        }

        /// <summary>
        /// Gets or sets the category under which the settings of <see cref="ClientHelper"/> are to be saved to the config file if the <see cref="PersistSettings"/> property is set to true.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value being assigned is a null or empty string.</exception>
        [Category("Persistence"),
        DefaultValue(DefaultSettingsCategory),
        Description("Category under which the settings of ClientHelper are to be saved to the config file if the PersistSettings property is set to true.")]
        public string SettingsCategory
        {
            get
            {
                return m_settingsCategory;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentNullException("value");

                m_settingsCategory = value;
            }
        }

        /// <summary>
        /// Gets or sets a boolean value that indicates whether the <see cref="ClientHelper"/> is currently enabled.
        /// </summary>
        /// <remarks>
        /// <see cref="Enabled"/> property is not be set by user-code directly.
        /// </remarks>
        [Browsable(false),
        EditorBrowsable(EditorBrowsableState.Never),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Enabled
        {
            get
            {
                if (m_remotingClient == null)
                    return false;
                else
                    return m_remotingClient.Enabled;
            }
            set
            {
                if (m_remotingClient != null)
                    m_remotingClient.Enabled = value;
            }
        }


        #endregion

        #region [ Methods ]

        /// <summary>
        /// Initializes the <see cref="ClientHelper"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="Initialize()"/> is to be called by user-code directly only if the <see cref="ClientHelper"/> is not consumed through the designer surface of the IDE.
        /// </remarks>
        public void Initialize()
        {
            if (!m_initialized)
            {
                LoadSettings();         // Load settings from the config file.
                m_initialized = true;   // Initialize only once.
            }
        }

        /// <summary>
        /// Performs necessary operations before the <see cref="ClientHelper"/> properties are initialized.
        /// </summary>
        /// <remarks>
        /// <see cref="BeginInit()"/> should never be called by user-code directly. This method exists solely for use 
        /// by the designer if the <see cref="ClientHelper"/> is consumed through the designer surface of the IDE.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void BeginInit()
        {
            if (!DesignMode)
            {
                try
                {
                    // Nothing needs to be done before component is initialized.
                }
                catch (Exception)
                {
                    // Prevent the IDE from crashing when component is in design mode.
                }
            }
        }

        /// <summary>
        /// Performs necessary operations after the <see cref="ClientHelper"/> properties are initialized.
        /// </summary>
        /// <remarks>
        /// <see cref="EndInit()"/> should never be called by user-code directly. This method exists solely for use 
        /// by the designer if the <see cref="ClientHelper"/> is consumed through the designer surface of the IDE.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void EndInit()
        {
            if (!DesignMode)
            {
                try
                {
                    Initialize();
                }
                catch (Exception)
                {
                    // Prevent the IDE from crashing when component is in design mode.
                }
            }
        }

        /// <summary>
        /// Saves settings for the <see cref="ClientHelper"/> to the config file if the <see cref="PersistSettings"/> property is set to true.
        /// </summary>
        /// <exception cref="ConfigurationErrorsException"><see cref="SettingsCategory"/> has a value of null or empty string.</exception>
        public void SaveSettings()
        {
            if (m_persistSettings)
            {
                // Ensure that settings category is specified.
                if (string.IsNullOrEmpty(m_settingsCategory))
                    throw new ConfigurationErrorsException("SettingsCategory property has not been set");

                // Save settings under the specified category.
                ConfigurationFile config = ConfigurationFile.Current;
                CategorizedSettingsElementCollection settings = config.Settings[m_settingsCategory];
                settings["Username", true].Update(m_username);
                settings["Password", true].Update(m_password);
                config.Save();
            }
        }

        /// <summary>
        /// Loads saved settings for the <see cref="ClientHelper"/> from the config file if the <see cref="PersistSettings"/> property is set to true.
        /// </summary>
        /// <exception cref="ConfigurationErrorsException"><see cref="SettingsCategory"/> has a value of null or empty string.</exception>
        public void LoadSettings()
        {
            if (m_persistSettings)
            {
                // Ensure that settings category is specified.
                if (string.IsNullOrEmpty(m_settingsCategory))
                    throw new ConfigurationErrorsException("SettingsCategory property has not been set");

                // Load settings from the specified category.
                ConfigurationFile config = ConfigurationFile.Current;
                CategorizedSettingsElementCollection settings = config.Settings[m_settingsCategory];
                settings.Add("Username", m_username, "Username to be used for authentication with the service.");
                settings.Add("Password", m_password, "Password to be used for authentication with the service.", true);
                Username = settings["Username"].ValueAs(m_username);
                Password = settings["Password"].ValueAs(m_password);
            }
        }

        /// <summary>
        /// Connects <see cref="RemotingClient"/> to <see cref="ServiceHelper.RemotingServer"/> and wait until authentication is complete.
        /// </summary>
        public void Connect()
        {
            if (m_remotingClient == null)
                throw new InvalidOperationException("RemotingClient property of ClientHelper component is not set");

            m_attemptReconnection = true;
            m_authenticationComplete = false;
            m_remotingClient.Connect();                                     // Wait for connection.

            while (!m_authenticationComplete && m_remotingClient.Enabled)
            {
                Thread.Sleep(100);
            } // Wait for authentication.
        }

        /// <summary>
        /// Disconnects <see cref="RemotingClient"/> from <see cref="ServiceHelper.RemotingServer"/>.
        /// </summary>
        public void Disconnect()
        {
            m_attemptReconnection = false;

            if (m_remotingClient != null)
                m_remotingClient.Disconnect();
        }

        /// <summary>
        /// Sends a request to the <see cref="ServiceHelper"/> using <see cref="RemotingClient"/>.
        /// </summary>
        /// <param name="request">Request text to be sent.</param>
        public void SendRequest(string request)
        {
            ClientRequest requestInstance = ClientRequest.Parse(request);

            if (requestInstance != null)
                SendRequest(requestInstance);
            else
                UpdateStatus(UpdateType.Warning, string.Format("Request command \"{0}\" is invalid\r\n\r\n", request));
        }

        /// <summary>
        /// Sends a request to the <see cref="ServiceHelper"/> using <see cref="RemotingClient"/>.
        /// </summary>
        /// <param name="request"><see cref="ClientRequest"/> object to be sent.</param>
        public void SendRequest(ClientRequest request)
        {
            m_remotingClient.SendAsync(request);
        }

        /// <summary>
        /// Raises the <see cref="ReceivedServiceUpdate"/> event.
        /// </summary>
        /// <param name="type">One of the <see cref="UpdateType"/> values.</param>
        /// <param name="update">Update message received.</param>
        protected virtual void OnReceivedServiceUpdate(UpdateType type, string update)
        {
            if (ReceivedServiceUpdate != null)
                ReceivedServiceUpdate(this, new EventArgs<UpdateType, string>(type, update));
        }

        /// <summary>
        /// Raises the <see cref="ReceivedServiceResponse"/> event.
        /// </summary>
        /// <param name="response"><see cref="ServiceResponse"/> received.</param>
        protected virtual void OnReceivedServiceResponse(ServiceResponse response)
        {
            if (ReceivedServiceResponse != null)
                ReceivedServiceResponse(this, new EventArgs<ServiceResponse>(response));
        }

        /// <summary>
        /// Raises the <see cref="ServiceStateChanged"/> event.
        /// </summary>
        /// <param name="state">New <see cref="ServiceState"/>.</param>
        protected virtual void OnServiceStateChanged(ObjectState<ServiceState> state)
        {
            if (ServiceStateChanged != null)
                ServiceStateChanged(this, new EventArgs<ObjectState<ServiceState>>(state));
        }

        /// <summary>
        /// Raises the <see cref="ProcessStateChanged"/> event.
        /// </summary>
        /// <param name="state">New <see cref="ServiceProcessState"/>.</param>
        protected virtual void OnProcessStateChanged(ObjectState<ServiceProcessState> state)
        {
            if (ProcessStateChanged != null)
                ProcessStateChanged(this, new EventArgs<ObjectState<ServiceProcessState>>(state));
        }

        /// <summary>
        /// Raises the <see cref="AuthenticationSuccess"/> event.
        /// </summary>
        protected virtual void OnAuthenticationSuccess()
        {
            m_authenticationComplete = true;
            if (AuthenticationSuccess != null)
                AuthenticationSuccess(this, EventArgs.Empty);
        }

        /// <summary>
        /// Raises the <see cref="AuthenticationFailure"/> event.
        /// </summary>
        protected virtual void OnAuthenticationFailure()
        {
            CancelEventArgs args = new CancelEventArgs(true);
            if (AuthenticationFailure != null)
                AuthenticationFailure(this, args);

            // Continue connection attempts if requested.
            if (args.Cancel)
            {
                m_attemptReconnection = false;
                m_authenticationComplete = true;
            }
        }

        /// <summary>
        /// Raises the <see cref="TelnetSessionEstablished"/> event.
        /// </summary>
        protected virtual void OnTelnetSessionEstablished()
        {
            if (TelnetSessionEstablished != null)
                TelnetSessionEstablished(this, EventArgs.Empty);
        }

        /// <summary>
        /// Raises the <see cref="TelnetSessionTerminated"/> event.
        /// </summary>
        protected virtual void OnTelnetSessionTerminated()
        {
            if (TelnetSessionTerminated != null)
                TelnetSessionTerminated(this, EventArgs.Empty);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="ClientHelper"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    // This will be done regardless of whether the object is finalized or disposed.
                    if (disposing)
                    {
                        // This will be done only when the object is disposed by calling Dispose().
                        Disconnect();
                        SaveSettings();
                        RemotingClient = null;
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        private void UpdateStatus(UpdateType type, string message, params object[] args)
        {
            OnReceivedServiceUpdate(type, string.Format(message, args));
        }

        private void RemotingClient_ConnectionAttempt(object sender, EventArgs e)
        {
            UpdateStatus(UpdateType.Information, "Connecting to {0}...\r\n\r\n", m_remotingClient.ServerUri);
        }

        private void RemotingClient_ConnectionEstablished(object sender, EventArgs e)
        {
            // Upon establishing connection with the service's communication client, we'll send our information to the
            // service so the service can keep track of all the client that are connected to its communication server.
            m_remotingClient.SendAsync(new ClientInfo(this));

            StringBuilder status = new StringBuilder();
            status.AppendFormat("Connected to {0}:", m_remotingClient.ServerUri);
            status.AppendLine();
            status.AppendLine();
            status.Append(m_remotingClient.Status);
            status.AppendLine();
            UpdateStatus(UpdateType.Information, status.ToString());
        }

        private void RemotingClient_ConnectionException(object sender, EventArgs<Exception> e)
        {
            StringBuilder status = new StringBuilder();
            TlsClient remotingClient;

            status.AppendFormat("Exception during connection attempt: {0}", e.Argument.Message);
            status.AppendLine();
            status.AppendLine();
            UpdateStatus(UpdateType.Alarm, status.ToString());

            remotingClient = m_remotingClient as TlsClient;

            if ((object)remotingClient != null)
                remotingClient.NetworkCredential = null;
        }

        private void RemotingClient_ConnectionTerminated(object sender, EventArgs e)
        {
            StringBuilder status = new StringBuilder();
            status.AppendFormat("Disconnected from {0}:", m_remotingClient.ServerUri);
            status.AppendLine();
            status.AppendLine();
            status.Append(m_remotingClient.Status);
            status.AppendLine();
            UpdateStatus(UpdateType.Warning, status.ToString());

            // Attempt reconnection on a separate thread.
            if (m_attemptReconnection)
                new Thread((ThreadStart)delegate
                {
                    Connect();
                }).Start();
        }

        private void RemotingClient_ReceiveDataComplete(object sender, EventArgs<byte[], int> e)
        {
            ServiceResponse response = null;
            Serialization.TryDeserialize(e.Argument1.BlockCopy(0, e.Argument2), SerializationFormat.Binary, out response);
            if (response != null)
            {
                switch (response.Type)
                {
                    case "UPDATECLIENTSTATUS-INFORMATION":
                        UpdateStatus(UpdateType.Information, response.Message);
                        break;
                    case "UPDATECLIENTSTATUS-WARNING":
                        UpdateStatus(UpdateType.Warning, response.Message);
                        break;
                    case "UPDATECLIENTSTATUS-ALARM":
                        UpdateStatus(UpdateType.Alarm, response.Message);
                        break;
                    case "AUTHENTICATIONSUCCESS":
                        OnAuthenticationSuccess();
                        break;
                    case "AUTHENTICATIONFAILURE":
                        OnAuthenticationFailure();
                        break;
                    case "SERVICESTATECHANGED":
                        if (response.Attachments.Count > 0)
                        {
                            ObjectState<ServiceState> state = response.Attachments[0] as ObjectState<ServiceState>;

                            if (state != null)
                            {
                                // Notify change in service state by raising an event.
                                OnServiceStateChanged(state);

                                // Provide a status update for change in state of the service.
                                UpdateType type = UpdateType.Information;
                                switch (state.CurrentState)
                                {
                                    case ServiceState.Stopped:
                                    case ServiceState.Paused:
                                    case ServiceState.Shutdown:
                                        type = UpdateType.Warning;
                                        break;
                                }
                                UpdateStatus(type, string.Format("State of service \"{0}\" has changed to \"{1}\".\r\n\r\n", state.ObjectName, state.CurrentState));
                            }
                        }
                        break;
                    case "PROCESSSTATECHANGED":
                        if (response.Attachments.Count > 0)
                        {
                            ObjectState<ServiceProcessState> state = response.Attachments[0] as ObjectState<ServiceProcessState>;

                            if (state != null)
                            {
                                // Notify change in process state by raising an event.
                                OnProcessStateChanged(state);

                                // Provide a status update for change in state of the service process.
                                UpdateType type = UpdateType.Information;
                                switch (state.CurrentState)
                                {
                                    case ServiceProcessState.Aborted:
                                    case ServiceProcessState.Exception:
                                        type = UpdateType.Alarm;
                                        break;

                                }
                                UpdateStatus(type, string.Format("State of process \"{0}\" has changed to \"{1}\".\r\n\r\n", state.ObjectName, state.CurrentState));
                            }
                        }
                        break;
                    case "TELNETSESSION":
                        switch (response.Message.ToUpper())
                        {
                            case "ESTABLISHED":
                                OnTelnetSessionEstablished();
                                break;
                            case "TERMINATED":
                                OnTelnetSessionTerminated();
                                break;
                        }
                        break;
                    default:
                        OnReceivedServiceResponse(response);
                        break;
                }
            }
        }

        #endregion

        #region [ Static ]

        /// <summary>
        /// Returns an <see cref="ClientRequestInfo"/> object for the specified <paramref name="requestCommand"/> that can be used 
        /// to invoke <see cref="ServiceHelper.ClientRequestHandlers"/> manually as if the request was sent by a <see cref="ClientHelper"/> remotely.
        /// </summary>
        /// <param name="requestCommand">Command for which an <see cref="ClientRequestInfo"/> object is to be created.</param>
        /// <returns>An <see cref="ClientRequestInfo"/> object.</returns>
        public static ClientRequestInfo PretendRequest(string requestCommand)
        {
            ClientRequest request = ClientRequest.Parse(requestCommand);
            ClientRequestInfo requestInfo = new ClientRequestInfo(new ClientInfo(), request);

            return requestInfo;
        }

        /// <summary>
        /// Attempts to parse an actionable reponse sent from the service.
        /// </summary>
        /// <param name="serviceResponse"><see cref="ServiceResponse"/> to test for actionable response.</param>
        /// <param name="sourceCommand">Command that invoked <paramref name="serviceResponse"/>.</param>
        /// <param name="responseSuccess">Boolean success state of <paramref name="serviceResponse"/>.</param>
        /// <returns><c>true</c> if actionable response was able to be parsed successfully; otherwise <c>false</c>.</returns>
        public static bool TryParseActionableResponse(ServiceResponse serviceResponse, out string sourceCommand, out bool responseSuccess)
        {
            bool parseSucceeded = false;

            sourceCommand = null;
            responseSuccess = false;

            try
            {
                string response = serviceResponse.Type;

                // Attempt to parse response message
                if (!string.IsNullOrWhiteSpace(response))
                {
                    // Reponse types are formatted as "Command:Success" or "Command:Failure"
                    string[] parts = response.Split(':');

                    if (parts.Length > 1)
                    {
                        sourceCommand = parts[0].Trim().ToTitleCase();
                        responseSuccess = (string.Compare(parts[1].Trim(), "Success", true) == 0);
                        parseSucceeded = true;
                    }
                }
            }
            catch
            {
                parseSucceeded = false;
            }

            return parseSucceeded;
        }

        #endregion
    }
}