﻿//******************************************************************************************************
//  PhasorDataConcentratorBase.cs - Gbtc
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
//  05/26/2009 - J. Ritchie Carroll
//       Generated original version of source code.
//  09/15/2009 - Stephen C. Wills
//       Added new header and license agreement.
//  11/25/2009 - Pinal C. Patel
//       Modified Start() to wait for initialization to complete for thread synchronization.
//  09/27/2010 - J. Ritchie Carroll
//       Modified phasor label generation to only include phase and type suffix if
//       it's not already applied.
//  04/14/2011 - Barb Motteler/ Jian (Ryan) Zuo
//      In StartDataChannel, moved EstablishPublicationChannel to before check for m_publishChannel
//      to resolve issue with data UDP data channel not starting on first command if "auto start"
//      is not checked.
//  12/04/2012 - J. Ritchie Carroll
//       Migrated to PhasorProtocolAdapters project.
//  12/13/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using GSF;
using GSF.Communication;
using GSF.Parsing;
using GSF.PhasorProtocols;
using GSF.PhasorProtocols.Anonymous;
using GSF.TimeSeries;
using GSF.TimeSeries.Adapters;
using GSF.TimeSeries.Statistics;
using GSF.Units;
using Timer = System.Timers.Timer;

namespace PhasorProtocolAdapters
{
    /// <summary>
    /// Represents an <see cref="IActionAdapter"/> used to generate and transmit concentrated stream
    /// of phasor measurements in a specific phasor protocol.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract class PhasorDataConcentratorBase : ActionAdapterBase
    {
        #region [ Design Notes ]

        // These design notes were written prior to the development of this class so they may not completely
        // represent its final design; however, the comments should provide insight into to the reasoning
        // behind the implementation of this class so they are included here for reference purposes.

        // J. Ritchie Carroll

        // **************************************************************************************************

        // We can't assume what devices will appear in the stream - therefore a device list is needed.
        // In addition, a list of active measurements needs to be loaded - that is, the measurements that
        // will make up the data in the device list.  Idealistically one could map any given number of
        // measurements to a device allowing a completely virtually defined device.

        // In the existing system the need for virtual devices was minimal - there is only one virtual
        // device, the EIRA PMU, which defines the calculated interconnection reference angle - this includes
        // an average interconnection frequency - hence you have a virtual device consisting entirely of
        // of composed measurement points. Normally you just want to retransmit the received device data
        // which is forwared as a cell in the combined outgoing data stream - this typically excludes any
        // digital or analog values - but there may be cases where this data should be retransmitted as well.

        // It is fairly straight forward to reverse the process of mapping device signals to measurements
        // using the existing signal references - this requires the definition of input devices to match
        // the definition of output devices. For ultimate flexibility however, you would allow any given
        // measurement to be mapped to a device definition created entirely by hand.

        // To further explore this idea, a normal case would be including a device in the outgoing data
        // stream as it is currently defined in the system. This would mean simply creating a measurement
        // list for this device based on its defined signal references - or one would just load the
        // measurements (filtered by need - i.e., excluding digitals and analogs if needed) for the device
        // as its currently defined. This seems fairly trivial - a simple check box to include an existing
        // device as-is in the outgoing data stream definition.

        // The interesting part will be tweaking the outgoing device definition - for simple definitions
        // the existing signal reference for a measurement will define its purpose in an outgoing device
        // device definition, but for ultimate flexibility any existing measurement can be mapped to a
        // any field in the device definition - this means the measurement will need a signal reference
        // that is defined when the measurement is mapped to the field - a new signal reference that exists
        // solely for this outgoing stream definition.

        // In the end a set of tables needs to exist that defines the outgoing data streams, the devices
        // that will appear in these streams (technically these do not need to already exist) and the
        // points that make up the field defintitions in these devices along with their signal references
        // that designate their destination field location - this will not necessarily be the perordained 
        // signal reference that was used to orginally map this field to a measurement - but rather an
        // outgoing data stream specific signal reference that exists for this measurement mapped into
        // this device.

        // This brings up an interesting notion - measurements in the system will not necessarily have a
        // one-to-one ratio with the outgoing field devices.  What this means is that a single measurement
        // point could be mapped to multiple locations within the same or multiple devices in any
        // variety of outgoing data streams. From a technical perspective as it relates to the measurement
        // concentration engine, a point will still have a destination frame based on its timestamp, but
        // it may have application at various locations (i.e., cell based devices) within that frame.

        // As a result a measurement will need to identify multiple destinations, that is, it may need to
        // track multiple signal references so that the measurement can be applied to all destination
        // field locations during the AssignMeasurementToFrame procedure of the data frame creation stage.

        // As the measurement is assigned to its destination frame by the concentration engine, the method
        // will need to loop through each signal reference assigned to the measurement. The method will then
        // obtain a reference to the data cell by its cell index and assign the measurement to the field
        // location based on the signal type and optional field index (e.g., phasor 1, 2, 3, etc.). This
        // should complete the operation of creating a data frame based on incoming measurements and leave
        // the data frame ready to publish in the next 1/30 of a second.

        // Suggested table definitions for the phasor data concentrator base class:

        //    - OutputStreamDevice          Stream ID, Name, ID, Analog Count, Digital Count, etc.
        //    - OutputStreamPhasor          Device ID, Kind (I or V), Name, Order, etc.
        //    - OutputStreamMeasurement     Device ID, MeasurementKey, Destination SignalReference

        // Proposed internal data structures used to collate information:

        // Protocol independent configuration frame that defines all output stream devices
        // Dictionary<MeasurementKey, SignalReference[]> <- multiple possible signal refs per measurement
        // SignalReference defines cell index of associated data cell and signal information

        #endregion

        #region [ Members ]

        // Fields
        private UdpServer m_dataChannel;
        private TcpServer m_commandChannel;
        private IServer m_publishChannel;
        private IConfigurationFrame m_configurationFrame;
        private ConfigurationFrame m_baseConfigurationFrame;
        private readonly ConcurrentDictionary<MeasurementKey, SignalReference[]> m_signalReferences;
        private readonly ConcurrentDictionary<SignalKind, string[]> m_generatedSignalReferenceCache;
        private readonly ConcurrentDictionary<Guid, string> m_connectionIDCache;
        private Timer m_commandChannelRestartTimer;
        private readonly object m_reinitializationLock;
        private long m_activeConnections;
        private LineFrequency m_nominalFrequency;
        private DataFormat m_dataFormat;
        private CoordinateFormat m_coordinateFormat;
        private uint m_currentScalingValue;
        private uint m_voltageScalingValue;
        private uint m_analogScalingValue;
        private uint m_digitalMaskValue;
        private bool m_autoPublishConfigurationFrame;
        private int m_lastConfigurationPublishMinute;
        private bool m_configurationFramePublished;
        private bool m_autoStartDataChannel;
        private bool m_processDataValidFlag;
        private bool m_addPhaseLabelSuffix;
        private char m_replaceWithSpaceChar;
        private ushort m_idCode;
        private long m_totalLatency;
        private long m_minimumLatency;
        private long m_maximumLatency;
        private long m_latencyMeasurements;
        private int m_hashCode;
        private bool m_useAdjustedValue;

        private long m_totalBytesSent;
        private long m_lifetimeMeasurements;
        private long m_minimumMeasurementsPerSecond;
        private long m_maximumMeasurementsPerSecond;
        private long m_totalMeasurementsPerSecond;
        private long m_measurementsPerSecondCount;
        private long m_measurementsInSecond;
        private long m_lastSecondsSinceEpoch;
        private long m_lifetimeTotalLatency;
        private long m_lifetimeMinimumLatency;
        private long m_lifetimeMaximumLatency;
        private long m_lifetimeLatencyMeasurements;

        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        protected PhasorDataConcentratorBase()
        {
            // Create a new signal reference dictionary indexed on measurement keys
            m_signalReferences = new ConcurrentDictionary<MeasurementKey, SignalReference[]>();

            // Create a cached signal reference dictionary for generated signal referencs
            m_generatedSignalReferenceCache = new ConcurrentDictionary<SignalKind, string[]>();

            // Create a new connection ID cache
            m_connectionIDCache = new ConcurrentDictionary<Guid, string>();

            // Lock used to reinitialize socket layer
            m_reinitializationLock = new object();

            // Synchrophasor protocols should default to millisecond resolution
            base.TimeResolution = Ticks.PerMillisecond;

            // Setup a timer for restarting the command channel if it fails
            m_commandChannelRestartTimer = new Timer(2000.0D);
            m_commandChannelRestartTimer.AutoReset = false;
            m_commandChannelRestartTimer.Enabled = false;
            m_commandChannelRestartTimer.Elapsed += m_commandChannelRestartTimer_Elapsed;
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets ID code defined for this <see cref="PhasorDataConcentratorBase"/> parsed from the <see cref="ActionAdapterBase.ConnectionString"/>.
        /// </summary>
        public ushort IDCode
        {
            get
            {
                return m_idCode;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if configuration frame should be automatically published at the top
        /// of each minute on the data channel.
        /// </summary>
        /// <remarks>
        /// By default if no command channel is defined, this flag will be <c>true</c>; otherwise if command channel
        /// is defined the flag will be <c>false</c>.
        /// </remarks>
        public bool AutoPublishConfigurationFrame
        {
            get
            {
                return m_autoPublishConfigurationFrame;
            }
            set
            {
                m_autoPublishConfigurationFrame = value;
            }
        }

        /// <summary>
        /// Gets the total number of active socket connections.
        /// </summary>
        public long ActiveConnections
        {
            get
            {
                return m_activeConnections;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if concentrator will automatically start data channel.
        /// </summary>
        /// <remarks>
        /// By default data channel will be started automatically, setting this flag to <c>false</c> will
        /// allow alternate methods of enabling and disabling the real-time data stream (e.g., this can
        /// be used to allow a remote to device to enable and disable data stream if the protocol supports
        /// such commands).
        /// </remarks>
        public bool AutoStartDataChannel
        {
            get
            {
                return m_autoStartDataChannel;
            }
            set
            {
                m_autoStartDataChannel = value;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if the data valid flag assignments should be processed during frame publication.
        /// </summary>
        /// <remarks>
        /// In cases where client applications ignore the data validity flag, setting this flag to <c>false</c> will
        /// provide a slight processing optimization, especially useful on very large data streams.
        /// </remarks>
        public bool ProcessDataValidFlag
        {
            get
            {
                return m_processDataValidFlag;
            }
            set
            {
                m_processDataValidFlag = value;
            }
        }

        /// <summary>
        /// Gets or sets the nominal <see cref="LineFrequency"/> defined for this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        public LineFrequency NominalFrequency
        {
            get
            {
                return m_nominalFrequency;
            }
            set
            {
                m_nominalFrequency = value;
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="GSF.PhasorProtocols.DataFormat"/> defined for this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        /// <remarks>
        /// Note that this value represents the default format that will be used if user has not specified a data format for an individual device.
        /// </remarks>
        public DataFormat DataFormat
        {
            get
            {
                return m_dataFormat;
            }
            set
            {
                m_dataFormat = value;
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="GSF.PhasorProtocols.CoordinateFormat"/> defined for this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        /// <remarks>
        /// Note that this value represents the default format that will be used if user has not specified a coordinate format for an individual device.
        /// </remarks>
        public CoordinateFormat CoordinateFormat
        {
            get
            {
                return m_coordinateFormat;
            }
            set
            {
                m_coordinateFormat = value;
            }
        }

        /// <summary>
        /// Gets or sets the integer scaling value to apply to current values published by this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        /// <remarks>
        /// Typically only the lower 24-bits will be used to scale current values in 10^–5 amperes per bit. Note that this value represents
        /// the default value that will be used if user has not specified a value for an individual device.
        /// </remarks>
        public uint CurrentScalingValue
        {
            get
            {
                return m_currentScalingValue;
            }
            set
            {
                m_currentScalingValue = value;
            }
        }

        /// <summary>
        /// Gets or sets the integer scaling value to apply to voltage values published by this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        /// <remarks>
        /// Typically only the lower 24-bits will be used to scale voltage values in 10^–5 volts per bit. Note that this value represents
        /// the default value that will be used if user has not specified a value for an individual device.
        /// </remarks>
        public uint VoltageScalingValue
        {
            get
            {
                return m_voltageScalingValue;
            }
            set
            {
                m_voltageScalingValue = value;
            }
        }

        /// <summary>
        /// Gets or sets the integer scaling value to apply to analog values published by this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        /// <remarks>
        /// Typically only the lower 24-bits will be used to scale analog values in 10^–5 units per bit. Note that this value represents
        /// the default value that will be used if user has not specified a value for an individual device.
        /// </remarks>
        public uint AnalogScalingValue
        {
            get
            {
                return m_analogScalingValue;
            }
            set
            {
                m_analogScalingValue = value;
            }
        }

        /// <summary>
        /// Gets or sets the digital mask value made available in configuration frames for use with digital values published by this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        /// <remarks>
        /// This value represents two mask words for use with digital status values. In IEEE C37.118 configuration frames, the two 16-bit words that make up a digital mask
        /// value are provided for each defined digital word. Note that this value represents the default value that will be used if user has not specified a value for an
        /// individual device. The low word will be used to indicate the normal status of the digital inputs by returning a 0 when exclusive ORed (XOR) with the status word.
        /// The high word will indicate the current valid inputs to the PMU by having a bit set in the binary position corresponding to the digital input and all other bits
        /// set to 0. If digital status words are used for something other than boolean status indications, the use is left to the user.
        /// </remarks>
        public uint DigitalMaskValue
        {
            get
            {
                return m_digitalMaskValue;
            }
            set
            {
                m_digitalMaskValue = value;
            }
        }

        /// <summary>
        /// Gets the minimum latency in milliseconds over the last test interval.
        /// </summary>
        public int MinimumLatency
        {
            get
            {
                return (int)Ticks.ToMilliseconds(m_minimumLatency);
            }
        }

        /// <summary>
        /// Gets the maximum latency in milliseconds over the last test interval.
        /// </summary>
        public int MaximumLatency
        {
            get
            {
                return (int)Ticks.ToMilliseconds(m_maximumLatency);
            }
        }

        /// <summary>
        /// Gets the average latency in milliseconds over the last test interval.
        /// </summary>
        public int AverageLatency
        {
            get
            {
                if (m_latencyMeasurements == 0)
                    return -1;

                return (int)Ticks.ToMilliseconds(m_totalLatency / m_latencyMeasurements);
            }
        }

        /// <summary>
        /// Gets the current number of known connected clients on the command channel.
        /// </summary>
        public int ConnectedClientCount
        {
            get
            {
                if (m_commandChannel != null && m_commandChannel.CurrentState == ServerState.Running)
                {
                    try
                    {
                        return m_commandChannel.ClientIDs.Length;
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                }

                return 0;
            }
        }

        /// <summary>
        /// Gets or sets the protocol specific <see cref="IConfigurationFrame"/> used to send to clients for protocol parsing.
        /// </summary>
        public virtual IConfigurationFrame ConfigurationFrame
        {
            get
            {
                return m_configurationFrame;
            }
            set
            {
                m_configurationFrame = value;
            }
        }

        /// <summary>
        /// Gets the protocol independent <see cref="GSF.PhasorProtocols.Anonymous.ConfigurationFrame"/> defined for this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        public ConfigurationFrame BaseConfigurationFrame
        {
            get
            {
                return m_baseConfigurationFrame;
            }
        }

        /// <summary>
        /// Gets or sets reference to <see cref="UdpServer"/> data channel, attaching and/or detaching to events as needed.
        /// </summary>
        protected UdpServer DataChannel
        {
            get
            {
                return m_dataChannel;
            }
            set
            {
                if (m_dataChannel != null)
                {
                    // Detach from events on existing data channel reference
                    m_dataChannel.ClientConnectingException -= m_dataChannel_ClientConnectingException;
                    m_dataChannel.ReceiveClientDataComplete -= m_dataChannel_ReceiveClientDataComplete;
                    m_dataChannel.SendClientDataException -= m_dataChannel_SendClientDataException;
                    m_dataChannel.ServerStarted -= m_dataChannel_ServerStarted;
                    m_dataChannel.ServerStopped -= m_dataChannel_ServerStopped;

                    if (m_dataChannel != value)
                        m_dataChannel.Dispose();
                }

                // Assign new data channel reference
                m_dataChannel = value;

                if (m_dataChannel != null)
                {
                    // Attach to events on new data channel reference
                    m_dataChannel.ClientConnectingException += m_dataChannel_ClientConnectingException;
                    m_dataChannel.ReceiveClientDataComplete += m_dataChannel_ReceiveClientDataComplete;
                    m_dataChannel.SendClientDataException += m_dataChannel_SendClientDataException;
                    m_dataChannel.ServerStarted += m_dataChannel_ServerStarted;
                    m_dataChannel.ServerStopped += m_dataChannel_ServerStopped;
                }
            }
        }

        /// <summary>
        /// Gets or sets reference to <see cref="TcpServer"/> command channel, attaching and/or detaching to events as needed.
        /// </summary>
        protected TcpServer CommandChannel
        {
            get
            {
                return m_commandChannel;
            }
            set
            {
                if (m_commandChannel != null)
                {
                    // Detach from events on existing command channel reference
                    m_commandChannel.ClientConnected -= m_commandChannel_ClientConnected;
                    m_commandChannel.ClientDisconnected -= m_commandChannel_ClientDisconnected;
                    m_commandChannel.ClientConnectingException -= m_commandChannel_ClientConnectingException;
                    m_commandChannel.ReceiveClientDataComplete -= m_commandChannel_ReceiveClientDataComplete;
                    m_commandChannel.SendClientDataException -= m_commandChannel_SendClientDataException;
                    m_commandChannel.ServerStarted -= m_commandChannel_ServerStarted;
                    m_commandChannel.ServerStopped -= m_commandChannel_ServerStopped;

                    if (m_commandChannel != value)
                        m_commandChannel.Dispose();
                }

                // Assign new command channel reference
                m_commandChannel = value;

                if (m_commandChannel != null)
                {
                    // Attach to events on new command channel reference
                    m_commandChannel.ClientConnected += m_commandChannel_ClientConnected;
                    m_commandChannel.ClientDisconnected += m_commandChannel_ClientDisconnected;
                    m_commandChannel.ClientConnectingException += m_commandChannel_ClientConnectingException;
                    m_commandChannel.ReceiveClientDataComplete += m_commandChannel_ReceiveClientDataComplete;
                    m_commandChannel.SendClientDataException += m_commandChannel_SendClientDataException;
                    m_commandChannel.ServerStarted += m_commandChannel_ServerStarted;
                    m_commandChannel.ServerStopped += m_commandChannel_ServerStopped;
                }
            }
        }

        /// <summary>
        /// Gets the flag indicating if this adapter supports temporal processing.
        /// </summary>
        /// <remarks>
        /// Since the concentrator is designed to open sockets and produce data streams, it is expected
        /// that this would not be desired in a temporal data streaming session.
        /// </remarks>
        public override bool SupportsTemporalProcessing
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the detailed status of this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        public override string Status
        {
            get
            {
                StringBuilder status = new StringBuilder();

                if (m_configurationFrame != null)
                {
                    status.AppendFormat("  Configuration frame size: {0} bytes", m_configurationFrame.BinaryLength);
                    status.AppendLine();
                }

                if (m_baseConfigurationFrame != null && m_baseConfigurationFrame.Cells != null)
                {
                    status.AppendFormat("  Total configured devices: {0}", m_baseConfigurationFrame.Cells.Count);
                    status.AppendLine();
                }

                if (m_signalReferences != null)
                {
                    status.AppendFormat(" Total device measurements: {0}", m_signalReferences.Count);
                    status.AppendLine();
                }

                status.AppendFormat(" Auto-publish config frame: {0}", m_autoPublishConfigurationFrame);
                status.AppendLine();
                status.AppendFormat("   Auto-start data channel: {0}", m_autoStartDataChannel);
                status.AppendLine();
                status.AppendFormat("       Data stream ID code: {0}", m_idCode);
                status.AppendLine();
                status.AppendFormat("         Nomimal frequency: {0}Hz", (int)m_nominalFrequency);
                status.AppendLine();
                status.AppendFormat("               Data format: {0}", m_dataFormat);
                status.AppendLine();
                status.AppendFormat("         Coordinate format: {0}", m_coordinateFormat);
                status.AppendLine();
                status.AppendFormat("    Minimum output latency: {0}ms over {1} tests", MinimumLatency, m_latencyMeasurements);
                status.AppendLine();
                status.AppendFormat("    Maximum output latency: {0}ms over {1} tests", MaximumLatency, m_latencyMeasurements);
                status.AppendLine();
                status.AppendFormat("    Average output latency: {0}ms over {1} tests", AverageLatency, m_latencyMeasurements);
                status.AppendLine();

                if (m_dataFormat == DataFormat.FixedInteger)
                {
                    status.AppendFormat("     Current scaling value: {0:00000000} ({1})", m_currentScalingValue, (m_currentScalingValue * 0.00001D).ToString("0.00000"));
                    status.AppendLine();
                    status.AppendFormat("     Voltage scaling value: {0:00000000} ({1})", m_voltageScalingValue, (m_voltageScalingValue * 0.00001D).ToString("0.00000"));
                    status.AppendLine();
                    status.AppendFormat("      Analog scaling value: {0:00000000} ({1})", m_analogScalingValue, (m_analogScalingValue * 0.00001D).ToString("0.00000"));
                    status.AppendLine();
                }

                status.AppendFormat("       Digital normal mask: {0} (big endian)", ByteEncoding.BigEndianBinary.GetString(BitConverter.GetBytes(m_digitalMaskValue.LowWord())));
                status.AppendLine();
                status.AppendFormat(" Digital valid inputs mask: {0} (big endian)", ByteEncoding.BigEndianBinary.GetString(BitConverter.GetBytes(m_digitalMaskValue.HighWord())));
                status.AppendLine();

                if (m_dataChannel != null)
                {
                    status.AppendLine();
                    status.AppendLine("Data Channel Status".CenterText(50));
                    status.AppendLine("-------------------".CenterText(50));
                    status.Append(m_dataChannel.Status);
                }

                if (m_commandChannel != null)
                {
                    status.AppendLine();
                    status.AppendLine("Command Channel Status".CenterText(50));
                    status.AppendLine("----------------------".CenterText(50));
                    status.Append(m_commandChannel.Status);
                }

                status.AppendLine();
                status.Append(base.Status);

                if (m_commandChannel != null)
                {
                    Guid[] clientIDs = m_commandChannel.ClientIDs;

                    if (clientIDs != null && clientIDs.Length > 0)
                    {
                        status.AppendLine();
                        status.AppendFormat("Command channel has {0} connected clients:\r\n\r\n", clientIDs.Length);

                        for (int i = 0; i < clientIDs.Length; i++)
                        {
                            status.AppendFormat("    {0}) {1}\r\n", i + 1, GetConnectionID(m_commandChannel, clientIDs[i]));
                        }

                        status.AppendLine();
                    }
                }

                return status.ToString();
            }
        }

        /// <summary>
        /// Gets the total number of bytes sent to clients of this output stream.
        /// </summary>
        public long TotalBytesSent
        {
            get
            {
                return m_totalBytesSent;
            }
        }

        /// <summary>
        /// Gets the total number of measurements processed through this output stream over the lifetime of the output stream.
        /// </summary>
        public long LifetimeMeasurements
        {
            get
            {
                return m_lifetimeMeasurements;
            }
        }

        /// <summary>
        /// Gets the minimum value of the measurements per second calculation.
        /// </summary>
        public long MinimumMeasurementsPerSecond
        {
            get
            {
                return m_minimumMeasurementsPerSecond;
            }
        }

        /// <summary>
        /// Gets the maximum value of the measurements per second calculation.
        /// </summary>
        public long MaximumMeasurementsPerSecond
        {
            get
            {
                return m_maximumMeasurementsPerSecond;
            }
        }

        /// <summary>
        /// Gets the average value of the measurements per second calculation.
        /// </summary>
        public long AverageMeasurementsPerSecond
        {
            get
            {
                if (m_measurementsPerSecondCount == 0L)
                    return 0L;

                return m_totalMeasurementsPerSecond / m_measurementsPerSecondCount;
            }
        }

        /// <summary>
        /// Gets the minimum latency calculated over the full lifetime of the output stream.
        /// </summary>
        public int LifetimeMinimumLatency
        {
            get
            {
                return (int)Ticks.ToMilliseconds(m_lifetimeMinimumLatency);
            }
        }

        /// <summary>
        /// Gets the maximum latency calculated over the full lifetime of the output stream.
        /// </summary>
        public int LifetimeMaximumLatency
        {
            get
            {
                return (int)Ticks.ToMilliseconds(m_lifetimeMaximumLatency);
            }
        }

        /// <summary>
        /// Gets the average latency calculated over the full lifetime of the output stream.
        /// </summary>
        public int LifetimeAverageLatency
        {
            get
            {
                if (m_lifetimeLatencyMeasurements == 0)
                    return -1;

                return (int)Ticks.ToMilliseconds(m_lifetimeTotalLatency / m_lifetimeLatencyMeasurements);
            }
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="PhasorDataConcentratorBase"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    if (disposing)
                    {
                        // Dispose command channel restart timer
                        if (m_commandChannelRestartTimer != null)
                        {
                            m_commandChannelRestartTimer.Elapsed -= m_commandChannelRestartTimer_Elapsed;
                            m_commandChannelRestartTimer.Dispose();
                        }
                        m_commandChannelRestartTimer = null;

                        // Dispose and detach from data and command channel events
                        this.DataChannel = null;
                        this.CommandChannel = null;
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        /// <summary>
        /// Starts the <see cref="PhasorDataConcentratorBase"/>, if it is not already running.
        /// </summary>
        public override void Start()
        {
            // Make sure we are stopped before attempting to start
            if (Enabled)
                Stop();

            m_lastConfigurationPublishMinute = -1;
            m_configurationFramePublished = false;

            // Start communications servers
            if ((m_autoStartDataChannel || m_commandChannel == null) && m_dataChannel != null && m_dataChannel.CurrentState == ServerState.NotRunning)
                m_dataChannel.Start();

            if (m_commandChannel != null && m_commandChannel.CurrentState == ServerState.NotRunning)
                m_commandChannel.Start();

            // Make sure publication channel is defined
            EstablishPublicationChannel();

            // Base action adapter gets started once data channel has been started (see m_dataChannel_ServerStarted)
            // so that the system doesn't attempt to start frame publication without an operational output data channel
            // when m_autoStartDataChannel is set to false. Otherwise if data is being published on command channel,
            // we go ahead and start concentration engine...
            if (m_publishChannel == m_commandChannel)
                base.Start();
        }

        /// <summary>
        /// Stops the <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        public override void Stop()
        {
            // Stop concentration engine
            base.Stop();

            // Stop data publication channel
            StopDataChannel();

            // Stop communications servers
            if (m_dataChannel != null)
                m_dataChannel.Stop();

            if (m_commandChannel != null)
                m_commandChannel.Stop();
        }

        /// <summary>
        /// Starts the <see cref="PhasorDataConcentratorBase"/> real-time data stream.
        /// </summary>
        /// <remarks>
        /// If <see cref="AutoStartDataChannel"/> is <c>false</c>, this method will allow host administrator
        /// to manually start the data channel, thus enabling the real-time data stream. If command channel
        /// is defined, it will be unaffected. 
        /// </remarks>
        [AdapterCommand("Manually starts the real-time data stream.", "Administrator", "Editor")]
        public virtual void StartDataChannel()
        {
            // Make sure publication channel is defined
            EstablishPublicationChannel();

            // Make sure publication channel has started
            if (m_publishChannel != null)
            {
                if (m_publishChannel.CurrentState == ServerState.NotRunning)
                {
                    try
                    {
                        m_publishChannel.Start();
                    }
                    catch (Exception ex)
                    {
                        OnProcessException(new InvalidOperationException("Failed to start publication channel: " + ex.Message, ex));
                    }
                }

                if (!Enabled)
                {
                    // Start concentration engine
                    base.Start();
                }
            }
        }

        // Define publication channel
        private void EstablishPublicationChannel()
        {
            // If data channel is not defined and command channel is defined system assumes
            // you want to make data available over TCP connection
            if (m_dataChannel == null && m_commandChannel != null)
                m_publishChannel = m_commandChannel;
            else
                m_publishChannel = m_dataChannel;
        }

        /// <summary>
        /// Stops the <see cref="PhasorDataConcentratorBase"/> real-time data stream.
        /// </summary>
        /// <remarks>
        /// This method will allow host administrator to manually stop the data channel, thus disabling
        /// the real-time data stream. If command channel is defined, it will be unaffected.
        /// </remarks>
        [AdapterCommand("Manually stops the real-time data stream.", "Administrator", "Editor")]
        public virtual void StopDataChannel()
        {
            // Undefine publication channel. This effectively haults socket based data publication.
            m_publishChannel = null;
        }

        /// <summary>
        /// Initializes <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();
            string errorMessage = "{0} is missing from Settings - Example: IDCode=235; dataChannel={{Port=0; Clients=localhost:8800}}";

            Dictionary<string, string> settings = Settings;
            string setting, dataChannel, commandChannel;

            // Load required parameters
            if (!settings.TryGetValue("IDCode", out setting))
                throw new ArgumentException(string.Format(errorMessage, "IDCode"));

            m_idCode = ushort.Parse(setting);
            settings.TryGetValue("dataChannel", out dataChannel);
            settings.TryGetValue("commandChannel", out commandChannel);

            // Data channel and/or command channel must be defined
            if (string.IsNullOrEmpty(dataChannel) && string.IsNullOrEmpty(commandChannel))
                throw new InvalidOperationException("A data channel or command channel must be defined for a concentrator.");

            // Load optional parameters
            if (settings.TryGetValue("autoPublishConfigFrame", out setting))
                m_autoPublishConfigurationFrame = setting.ParseBoolean();
            else
                m_autoPublishConfigurationFrame = string.IsNullOrEmpty(commandChannel);

            if (settings.TryGetValue("autoStartDataChannel", out setting))
                m_autoStartDataChannel = setting.ParseBoolean();
            else
                m_autoStartDataChannel = true;

            if (settings.TryGetValue("nominalFrequency", out setting))
                m_nominalFrequency = (LineFrequency)int.Parse(setting);
            else
                m_nominalFrequency = LineFrequency.Hz60;

            if (settings.TryGetValue("dataFormat", out setting))
                m_dataFormat = (DataFormat)Enum.Parse(typeof(DataFormat), setting, true);
            else
                m_dataFormat = DataFormat.FloatingPoint;

            if (settings.TryGetValue("coordinateFormat", out setting))
                m_coordinateFormat = (CoordinateFormat)Enum.Parse(typeof(CoordinateFormat), setting, true);
            else
                m_coordinateFormat = CoordinateFormat.Polar;

            if (settings.TryGetValue("currentScalingValue", out setting))
            {
                if (!uint.TryParse(setting, out m_currentScalingValue))
                    m_currentScalingValue = unchecked((uint)int.Parse(setting));
            }
            else
                m_currentScalingValue = 2423U;

            if (settings.TryGetValue("voltageScalingValue", out setting))
            {
                if (!uint.TryParse(setting, out m_voltageScalingValue))
                    m_voltageScalingValue = unchecked((uint)int.Parse(setting));
            }
            else
                m_voltageScalingValue = 2725785U;

            if (settings.TryGetValue("analogScalingValue", out setting))
            {
                if (!uint.TryParse(setting, out m_analogScalingValue))
                    m_analogScalingValue = unchecked((uint)int.Parse(setting));
            }
            else
                m_analogScalingValue = 1373291U;

            if (settings.TryGetValue("digitalMaskValue", out setting))
            {
                if (!uint.TryParse(setting, out m_digitalMaskValue))
                    m_digitalMaskValue = unchecked((uint)int.Parse(setting));
            }
            else
                m_digitalMaskValue = Word.MakeDword(0xFFFF, 0x0000);

            if (settings.TryGetValue("processDataValidFlag", out setting))
                m_processDataValidFlag = setting.ParseBoolean();
            else
                m_processDataValidFlag = true;

            if (settings.TryGetValue("addPhaseLabelSuffix", out setting))
                m_addPhaseLabelSuffix = setting.ParseBoolean();
            else
                m_addPhaseLabelSuffix = true;

            if (settings.TryGetValue("replaceWithSpaceChar", out setting))
            {
                if (!string.IsNullOrWhiteSpace(setting) && setting.Length > 0)
                    m_replaceWithSpaceChar = setting[0];
                else
                    m_replaceWithSpaceChar = Char.MinValue;
            }
            else
            {
                m_replaceWithSpaceChar = Char.MinValue;
            }

            if (settings.TryGetValue("useAdjustedValue", out setting))
            {
                m_useAdjustedValue = Boolean.Parse(setting);
            }
            else
            {
                m_useAdjustedValue = true;
            }

            // Initialize data channel if defined
            if (!string.IsNullOrEmpty(dataChannel))
                this.DataChannel = new UdpServer(dataChannel);
            else
                this.DataChannel = null;

            // Initialize command channel if defined
            if (!string.IsNullOrEmpty(commandChannel))
                this.CommandChannel = new TcpServer(commandChannel);
            else
                this.CommandChannel = null;

            // Create the configuration frame
            UpdateConfiguration();

            // Register with the statistics engine
            StatisticsEngine.Register(this, "OutputStream", "OS");
            StatisticsEngine.Calculated += (sender, args) => ResetMeasurementsPerSecondCounters();
        }

        /// <summary>
        /// Reloads the configuration for this <see cref="PhasorDataConcentratorBase"/>.
        /// </summary>
        [AdapterCommand("Reloads the phasor data concentrator configuration.", "Administrator", "Editor")]
        public void UpdateConfiguration()
        {
            const int labelLength = 16;
            Dictionary<string, int> signalCellIndexes = new Dictionary<string, int>();
            SignalReference signal;
            SignalReference[] signals;
            MeasurementKey measurementKey;
            PhasorType type;
            AnalogType analogType;
            char phase;
            string label, scale;
            uint scalingValue;
            int order;

            // Define a protocol independent configuration frame
            m_baseConfigurationFrame = new ConfigurationFrame(m_idCode, DateTime.UtcNow.Ticks, (ushort)base.FramesPerSecond);

            // Define configuration cells (i.e., PMU's that will appear in outgoing data stream)
            foreach (DataRow deviceRow in DataSource.Tables["OutputStreamDevices"].Select(string.Format("ParentID={0}", ID), "LoadOrder"))
            {
                try
                {
                    // Get device ID and ID code
                    int deviceID = int.Parse(deviceRow["ID"].ToString());
                    ushort idCode = ushort.Parse(deviceRow["IDCode"].ToString());

                    // If number was never assigned or is invalid, we fall back on unique database record ID
                    if (idCode == 0)
                        idCode = unchecked((ushort)deviceID);

                    // Create a new configuration cell
                    ConfigurationCell cell = new ConfigurationCell(m_baseConfigurationFrame, idCode);

                    // Assign user selected data and coordinate formats, derived classes can change
                    string formatString;

                    formatString = deviceRow["PhasorDataFormat"].ToNonNullString(m_dataFormat.ToString());
                    cell.PhasorDataFormat = (DataFormat)Enum.Parse(typeof(DataFormat), string.IsNullOrEmpty(formatString) ? m_dataFormat.ToString() : formatString, true);

                    formatString = deviceRow["FrequencyDataFormat"].ToNonNullString(m_dataFormat.ToString());
                    cell.FrequencyDataFormat = (DataFormat)Enum.Parse(typeof(DataFormat), string.IsNullOrEmpty(formatString) ? m_dataFormat.ToString() : formatString, true);

                    formatString = deviceRow["AnalogDataFormat"].ToNonNullString(m_dataFormat.ToString());
                    cell.AnalogDataFormat = (DataFormat)Enum.Parse(typeof(DataFormat), string.IsNullOrEmpty(formatString) ? m_dataFormat.ToString() : formatString, true);

                    formatString = deviceRow["CoordinateFormat"].ToNonNullString(m_coordinateFormat.ToString());
                    cell.PhasorCoordinateFormat = (CoordinateFormat)Enum.Parse(typeof(CoordinateFormat), string.IsNullOrEmpty(formatString) ? m_coordinateFormat.ToString() : formatString, true);

                    // Assign device identification labels
                    cell.IDLabel = deviceRow["Name"].ToString().TruncateRight(cell.IDLabelLength).Trim();
                    label = deviceRow["Acronym"].ToString().TruncateRight(cell.MaximumStationNameLength).Trim();

                    // Station name is serialized to configuration frame
                    cell.StationName = label;

                    // Define all the phasors configured for this device
                    foreach (DataRow phasorRow in DataSource.Tables["OutputStreamDevicePhasors"].Select(string.Format("OutputStreamDeviceID={0}", deviceID), "LoadOrder"))
                    {
                        order = int.Parse(phasorRow["LoadOrder"].ToNonNullString("0"));
                        label = phasorRow["Label"].ToNonNullString("Phasor " + order).Trim().TruncateRight(labelLength);
                        type = phasorRow["Type"].ToNonNullString("V").Trim().ToUpper().StartsWith("V") ? PhasorType.Voltage : PhasorType.Current;
                        phase = phasorRow["Phase"].ToNonNullString("+").Trim().ToUpper()[0];
                        scale = phasorRow["ScalingValue"].ToNonNullString("0");

                        if (m_replaceWithSpaceChar != Char.MinValue)
                            label = label.Replace(m_replaceWithSpaceChar, ' ');

                        // Scale can be defined as a negative value in database, so check both formatting styles
                        if (!uint.TryParse(scale, out scalingValue))
                            scalingValue = unchecked((uint)int.Parse(scale));

                        // Choose stream defined default value if no scaling value was defined
                        if (scalingValue == 0)
                            scalingValue = (type == PhasorType.Voltage ? m_voltageScalingValue : m_currentScalingValue);

                        cell.PhasorDefinitions.Add(
                            new PhasorDefinition(
                                cell,
                                GeneratePhasorLabel(label, phase, type),
                                scalingValue,
                                type,
                                null));
                    }

                    // Add frequency definition
                    label = string.Format("{0} Freq", cell.IDLabel.TruncateRight(labelLength - 5)).Trim();
                    cell.FrequencyDefinition = new FrequencyDefinition(cell, label);

                    // Optionally define all the analogs configured for this device
                    if (DataSource.Tables.Contains("OutputStreamDeviceAnalogs"))
                    {
                        foreach (DataRow analogRow in DataSource.Tables["OutputStreamDeviceAnalogs"].Select(string.Format("OutputStreamDeviceID={0}", deviceID), "LoadOrder"))
                        {
                            order = int.Parse(analogRow["LoadOrder"].ToNonNullString("0"));
                            label = analogRow["Label"].ToNonNullString("Analog " + order).Trim().TruncateRight(labelLength);
                            analogType = (AnalogType)int.Parse(analogRow["Type"].ToNonNullString("0"));
                            scale = analogRow["ScalingValue"].ToNonNullString("0");

                            if (m_replaceWithSpaceChar != Char.MinValue)
                                label = label.Replace(m_replaceWithSpaceChar, ' ');

                            // Scale can be defined as a negative value in database, so check both formatting styles
                            if (!uint.TryParse(scale, out scalingValue))
                                scalingValue = unchecked((uint)int.Parse(scale));

                            cell.AnalogDefinitions.Add(
                                new AnalogDefinition(
                                    cell,
                                    label,
                                    scalingValue == 0 ? m_analogScalingValue : scalingValue,
                                    analogType));
                        }
                    }

                    // Optionally define all the digitals configured for this device
                    if (DataSource.Tables.Contains("OutputStreamDeviceDigitals"))
                    {
                        foreach (DataRow digitalRow in DataSource.Tables["OutputStreamDeviceDigitals"].Select(string.Format("OutputStreamDeviceID={0}", deviceID), "LoadOrder"))
                        {
                            order = int.Parse(digitalRow["LoadOrder"].ToNonNullString("0"));
                            scale = digitalRow["MaskValue"].ToNonNullString("0");

                            // IEEE C37.118 digital labels are defined with all 16-labels (one for each bit) in one large formatted string
                            label = digitalRow["Label"].ToNonNullString("Digital " + order).Trim().TruncateRight(labelLength * 16);

                            if (m_replaceWithSpaceChar != Char.MinValue)
                                label = label.Replace(m_replaceWithSpaceChar, ' ');

                            // Mask can be defined as a negative value in database, so check both formatting styles
                            if (!uint.TryParse(scale, out scalingValue))
                                scalingValue = unchecked((uint)int.Parse(scale));

                            cell.DigitalDefinitions.Add(
                                new DigitalDefinition(
                                    cell,
                                    label,
                                    scalingValue == 0 ? m_digitalMaskValue : scalingValue));
                        }
                    }

                    m_baseConfigurationFrame.Cells.Add(cell);
                }
                catch (Exception ex)
                {
                    OnProcessException(new InvalidOperationException(string.Format("Failed to define output stream device \"{0}\" due to exception: {1}", deviceRow["Acronym"].ToString().Trim(), ex.Message), ex));
                }
            }

            OnStatusMessage("Defined {0} output stream devices...", m_baseConfigurationFrame.Cells.Count);

            // Clear any existing signal references
            m_signalReferences.Clear();

            // Define measurement to signals cross reference dictionary
            foreach (DataRow measurementRow in DataSource.Tables["OutputStreamMeasurements"].Select(string.Format("AdapterID={0}", ID)))
            {
                try
                {
                    // Create a new signal reference
                    signal = new SignalReference(measurementRow["SignalReference"].ToString());

                    // Lookup cell index by acronym - doing this work upfront will save a huge amount
                    // of work during primary measurement sorting
                    if (!signalCellIndexes.TryGetValue(signal.Acronym, out signal.CellIndex))
                    {
                        // We cache these indices locally to speed up initialization as we'll be
                        // requesting them for the same devices over and over
                        signal.CellIndex = m_baseConfigurationFrame.Cells.IndexOfStationName(signal.Acronym);
                        signalCellIndexes.Add(signal.Acronym, signal.CellIndex);
                    }

                    // No need to define this measurement for sorting unless it has a destination in the outgoing frame
                    if (signal.CellIndex > -1)
                    {
                        // Get historian field
                        string historian = measurementRow["Historian"].ToNonNullString();
                        string pointID = measurementRow["PointID"].ToString();

                        // Define measurement key
                        if (!string.IsNullOrEmpty(historian))
                        {
                            measurementKey = new MeasurementKey(Guid.Empty, uint.Parse(pointID), historian);
                        }
                        else
                        {
                            DataTable activeMeasurements = DataSource.Tables["ActiveMeasurements"];
                            DataRow[] activeMeasurementRows = new DataRow[0];

                            object activeMeasurementSignalID = null;
                            object activeMeasurementID = null;

                            if ((object)activeMeasurements != null)
                                activeMeasurementRows = activeMeasurements.Select(string.Format("ID LIKE '*:{0}'", pointID));

                            if (activeMeasurementRows.Length == 1)
                            {
                                activeMeasurementSignalID = activeMeasurementRows[0]["SignalID"];
                                activeMeasurementID = activeMeasurementRows[0]["ID"];
                            }

                            // If we still can't find the measurement key, now is the time to give up
                            if (activeMeasurementSignalID == null && activeMeasurementID == null)
                                throw new Exception(string.Format("Cannot find measurement key for measurement with pointID {0}", pointID));

                            measurementKey = MeasurementKey.Parse(activeMeasurementID.ToString(), Guid.Parse(activeMeasurementRows[0]["SignalID"].ToString()));
                        }

                        // It is possible, but not as common, that a single measurement will have multiple destinations
                        // within an outgoing data stream frame, hence the following
                        signals = m_signalReferences.GetOrAdd(measurementKey, null as SignalReference[]);

                        if ((object)signals == null)
                        {
                            // Add new signal to new collection
                            signals = new SignalReference[1];
                            signals[0] = signal;
                        }
                        else
                        {
                            // Add a new signal to existing collection
                            List<SignalReference> signalList = new List<SignalReference>(signals);
                            signalList.Add(signal);
                            signals = signalList.ToArray();
                        }

                        m_signalReferences[measurementKey] = signals;
                    }
                }
                catch (Exception ex)
                {
                    OnProcessException(new InvalidOperationException(string.Format("Failed to associate measurement key to signal reference \"{0}\" due to exception: {1}", measurementRow["SignalReference"].ToString().Trim(), ex.Message), ex));
                }
            }

            // Assign action adapter input measurement keys - this assigns the expected measurements per frame needed
            // by the concentration engine for preemptive publication 
            InputMeasurementKeys = m_signalReferences.Keys.ToArray();

            // Allow for spaces in output stream device names if a replacement character has been defined for spaces
            if (m_replaceWithSpaceChar != Char.MinValue)
            {
                foreach (ConfigurationCell cell in m_baseConfigurationFrame.Cells)
                {
                    cell.StationName = cell.StationName.Replace(m_replaceWithSpaceChar, ' ');
                }
            }

            // Create a new protocol specific configuration frame
            m_configurationFrame = CreateNewConfigurationFrame(m_baseConfigurationFrame);

            // Cache new protocol specific configuration frame
            CacheConfigurationFrame(m_configurationFrame, Name);
        }

        // Generate a more descriptive phasor label including line phase and phasor type
        private string GeneratePhasorLabel(string phasorLabel, char phase, PhasorType type)
        {
            StringBuilder phaseSuffix = new StringBuilder();

            if (string.IsNullOrWhiteSpace(phasorLabel))
                phasorLabel = "Phasor";

            if (m_addPhaseLabelSuffix)
            {
                string suffix = phasorLabel.TruncateLeft(4).TruncateRight(3).ToUpper();
                bool appended = false;

                // Add phase suffix if it's not already there
                switch (phase)
                {
                    case '+':   // Positive sequence
                        if (suffix != " +S")
                        {
                            phaseSuffix.Append(" +S");
                            appended = true;
                        }
                        break;
                    case '-':   // Negative sequence
                        if (suffix != " -S")
                        {
                            phaseSuffix.Append(" -S");
                            appended = true;
                        }
                        break;
                    case '0':   // Zero sequence
                        if (suffix != " 0S")
                        {
                            phaseSuffix.Append(" 0S");
                            appended = true;
                        }
                        break;
                    case 'A':   // A-Phase
                        if (suffix != " AP")
                        {
                            phaseSuffix.Append(" AP");
                            appended = true;
                        }
                        break;
                    case 'B':   // B-Phase
                        if (suffix != " BP")
                        {
                            phaseSuffix.Append(" BP");
                            appended = true;
                        }
                        break;
                    case 'C':   // C-Phase
                        if (suffix != " CP")
                        {
                            phaseSuffix.Append(" CP");
                            appended = true;
                        }
                        break;
                }

                if (appended)
                {
                    // Return label with appended phase suffix
                    phaseSuffix.Append(type == PhasorType.Voltage ? 'V' : 'I');
                    return phasorLabel.TruncateRight(12) + phaseSuffix;
                }
            }

            // Return original label
            return phasorLabel;
        }

        /// <summary>
        /// Resets the counters for the lifetime statistics without interrupting the adapter's operations.
        /// </summary>
        [AdapterCommand("Resets the counters for the lifetime statistics without interrupting the adapter's operations.", "Administrator", "Editor")]
        public virtual void ResetLifetimeCounters()
        {
            m_lifetimeMeasurements = 0L;
            m_totalBytesSent = 0L;
            m_lifetimeTotalLatency = 0L;
            m_lifetimeMinimumLatency = 0L;
            m_lifetimeMaximumLatency = 0L;
            m_lifetimeLatencyMeasurements = 0L;
        }

        /// <summary>
        /// Queues a single measurement for processing.
        /// </summary>
        /// <param name="measurement">Measurement to queue for processing.</param>
        public override void QueueMeasurementForProcessing(IMeasurement measurement)
        {
            QueueMeasurementsForProcessing(new[] { measurement });
        }

        /// <summary>
        /// Queues a collection of measurements for processing.
        /// </summary>
        /// <param name="measurements">Collection of measurements to queue for processing.</param>
        public override void QueueMeasurementsForProcessing(IEnumerable<IMeasurement> measurements)
        {
            List<IMeasurement> inputMeasurements = new List<IMeasurement>();
            SignalReference[] signals;

            foreach (IMeasurement measurement in measurements)
            {
                // We assign signal reference to measurement in advance since we are using this as a filter
                // anyway, this will save a lookup later during measurement assignment to frame...
                if (m_signalReferences.TryGetValue(measurement.Key, out signals))
                {
                    // Loop through each signal reference defined for this measurement - this handles
                    // the case where there can be more than one destination for a measurement within
                    // an outgoing phasor data frame
                    foreach (SignalReference signal in signals)
                    {
                        inputMeasurements.Add(new SignalReferenceMeasurement(measurement, signal));
                    }
                }
            }

            if (inputMeasurements.Count > 0)
                SortMeasurements(inputMeasurements);
        }

        /// <summary>
        /// Assign <see cref="IMeasurement"/> to its <see cref="IFrame"/>.
        /// </summary>
        /// <param name="frame"><see cref="IFrame"/> to assign <paramref name="measurement"/> to.</param>
        /// <param name="measurement"><see cref="IMeasurement"/> to assign to <paramref name="frame"/>.</param>
        /// <returns><c>true</c> if <see cref="IMeasurement"/> was successfully assigned to its <see cref="IFrame"/>.</returns>
        /// <remarks>
        /// In simple concentration scenarios all you need to do is assign a measurement to its frame based on
        /// time. In the case of a phasor data concentrator you need to assign a measurement to its particular
        /// location in its <see cref="IDataFrame"/> - so this method overrides the default behavior in order
        /// to accomplish this task.
        /// </remarks>
        protected override void AssignMeasurementToFrame(IFrame frame, IMeasurement measurement)
        {
            ConcurrentDictionary<MeasurementKey, IMeasurement> measurements = frame.Measurements;

            // Make sure the measurement is a "SignalReferenceMeasurement" (it should be)
            SignalReferenceMeasurement signalMeasurement = measurement as SignalReferenceMeasurement;
            IDataFrame dataFrame = frame as IDataFrame;

            if ((object)signalMeasurement != null && dataFrame != null)
            {
                PhasorValueCollection phasorValues;
                SignalReference signal = signalMeasurement.SignalReference;
                IDataCell dataCell = dataFrame.Cells[signal.CellIndex];
                int signalIndex = signal.Index;
                double signalValue = m_useAdjustedValue ? signalMeasurement.AdjustedValue : signalMeasurement.Value;

                // Assign measurement to its destination field in the data cell based on signal type
                switch (signal.Kind)
                {
                    case SignalKind.Angle:
                        // Assign "phase angle" measurement to data cell
                        phasorValues = dataCell.PhasorValues;
                        if (phasorValues.Count >= signalIndex)
                            phasorValues[signalIndex - 1].Angle = Angle.FromDegrees(signalValue);
                        break;
                    case SignalKind.Magnitude:
                        // Assign "phase magnitude" measurement to data cell
                        phasorValues = dataCell.PhasorValues;
                        if (phasorValues.Count >= signalIndex)
                            phasorValues[signalIndex - 1].Magnitude = signalValue;
                        break;
                    case SignalKind.Frequency:
                        // Assign "frequency" measurement to data cell
                        dataCell.FrequencyValue.Frequency = signalValue;
                        break;
                    case SignalKind.DfDt:
                        // Assign "dF/dt" measurement to data cell
                        dataCell.FrequencyValue.DfDt = signalValue;
                        break;
                    case SignalKind.Status:
                        // Assign "common status flags" measurement to data cell
                        dataCell.CommonStatusFlags = unchecked((uint)signalValue);

                        // Assign by arrival sorting flag for bad synchronization
                        if (!dataCell.SynchronizationIsValid && AllowSortsByArrival && !IgnoreBadTimestamps)
                            dataCell.DataSortingType = DataSortingType.ByArrival;
                        break;
                    case SignalKind.Digital:
                        // Assign "digital" measurement to data cell
                        DigitalValueCollection digitalValues = dataCell.DigitalValues;
                        if (digitalValues.Count >= signalIndex)
                            digitalValues[signalIndex - 1].Value = unchecked((ushort)signalValue);
                        break;
                    case SignalKind.Analog:
                        // Assign "analog" measurement to data cell
                        AnalogValueCollection analogValues = dataCell.AnalogValues;
                        if (analogValues.Count >= signalIndex)
                            analogValues[signalIndex - 1].Value = signalValue;
                        break;
                }

                // So that we can accurately track the total measurements that were sorted into this frame,
                // we also assign measurement to frame's measurement dictionary - this is important since
                // in downsampling scenarios more than one of the same measurement can be sorted into a frame
                // but this only needs to be counted as "one" sort so that when preemptive publishing is
                // enabled you can compare expected measurements to sorted measurements...
                measurements[measurement.Key] = measurement;

                return;
            }

            // This is not expected to occur - but just in case
            if ((object)signalMeasurement == null && measurement != null)
                OnProcessException(new InvalidCastException(string.Format("Attempt was made to assign an invalid measurement to phasor data concentration frame, expected a \"SignalReferenceMeasurement\" but received a \"{0}\"", measurement.GetType().Name)));

            if (dataFrame == null && frame != null)
                OnProcessException(new InvalidCastException(string.Format("During measurement assignment, incoming frame was not a phasor data concentration frame, expected a type derived from \"IDataFrame\" but received a \"{0}\"", frame.GetType().Name)));
        }

        /// <summary>
        /// Publish <see cref="IFrame"/> of time-aligned collection of <see cref="IMeasurement"/> values that arrived within the
        /// concentrator's defined <see cref="ConcentratorBase.LagTime"/>.
        /// </summary>
        /// <param name="frame"><see cref="IFrame"/> of measurements with the same timestamp that arrived within <see cref="ConcentratorBase.LagTime"/> that are ready for processing.</param>
        /// <param name="index">Index of <see cref="IFrame"/> within a second ranging from zero to <c><see cref="ConcentratorBase.FramesPerSecond"/> - 1</c>.</param>
        protected override void PublishFrame(IFrame frame, int index)
        {
            IDataFrame dataFrame = frame as IDataFrame;

            if (dataFrame != null && m_publishChannel != null)
            {
                byte[] image;

                // Send the configuration frame at the top of each minute if the class has been configured
                // to automatically publish the configuration frame over the data channel
                if (m_autoPublishConfigurationFrame)
                {
                    DateTime frameTime = dataFrame.Timestamp;

                    if (frameTime.Second == 0)
                    {
                        if (frameTime.Minute != m_lastConfigurationPublishMinute)
                        {
                            m_lastConfigurationPublishMinute = frameTime.Minute;
                            m_configurationFramePublished = false;
                        }

                        if (!m_configurationFramePublished)
                        {
                            // Publish configuration frame binary image
                            m_configurationFramePublished = true;
                            m_configurationFrame.Timestamp = dataFrame.Timestamp;

                            image = m_configurationFrame.BinaryImage();
                            m_publishChannel.MulticastAsync(image, 0, image.Length);
                            m_totalBytesSent += image.Length;

                            // Sleep for a moment between config frame and data frame transmissions
                            Thread.Sleep(1);
                        }
                    }
                }

                // If the expected values did not arrive for a device, we mark the data as invalid
                if (m_processDataValidFlag)
                {
                    foreach (IDataCell cell in dataFrame.Cells)
                    {
                        if (!cell.AllValuesAssigned)
                            cell.DataIsValid = false;
                    }
                }

                // Publish data frame binary image
                image = dataFrame.BinaryImage();
                m_publishChannel.MulticastAsync(image, 0, image.Length);
                m_totalBytesSent += image.Length;

                // Track latency statistics against system time - in order for these statistics
                // to be useful, the local clock must be fairly accurate
                long latency = DateTime.UtcNow.Ticks - (long)dataFrame.Timestamp;

                if (m_minimumLatency > latency || m_minimumLatency == 0)
                    m_minimumLatency = latency;

                if (m_maximumLatency < latency || m_maximumLatency == 0)
                    m_maximumLatency = latency;

                m_totalLatency += latency;
                m_latencyMeasurements++;

                if (m_lifetimeMinimumLatency > latency || m_lifetimeMinimumLatency == 0)
                    m_lifetimeMinimumLatency = latency;

                if (m_lifetimeMaximumLatency < latency || m_lifetimeMaximumLatency == 0)
                    m_lifetimeMaximumLatency = latency;

                m_lifetimeTotalLatency += latency;
                m_lifetimeLatencyMeasurements++;

                // Track measurement count and throughput statistics
                int measurementCount = frame.Measurements.Count;
                m_lifetimeMeasurements += measurementCount;
                UpdateMeasurementsPerSecond(measurementCount);
            }
        }

        private void ReinitializeSocketLayer(object state)
        {
            if (!m_disposed && Monitor.TryEnter(m_reinitializationLock))
            {
                bool retry = false;

                try
                {
                    Stop();

                    string commandChannelConfig = null, dataChannelConfig = null;

                    if ((object)m_dataChannel != null)
                    {
                        // Get current configuration string
                        dataChannelConfig = m_dataChannel.ConfigurationString;

                        // Dispose the existing data channel
                        this.DataChannel = null;
                    }

                    if ((object)m_commandChannel != null)
                    {
                        // Get current configuration string
                        commandChannelConfig = m_commandChannel.ConfigurationString;

                        // Dispose the existing command channel
                        this.CommandChannel = null;
                    }

                    // Wait a moment to let sockets release
                    Thread.Sleep(1000);

                    // Clear existing cache
                    m_connectionIDCache.Clear();

                    // Reinitialize data channel, if defined
                    if (!string.IsNullOrWhiteSpace(dataChannelConfig))
                        this.DataChannel = new UdpServer(dataChannelConfig);

                    // Reinitialize command channel, if defined
                    if (!string.IsNullOrWhiteSpace(commandChannelConfig))
                        this.CommandChannel = new TcpServer(commandChannelConfig);

                    Start();
                }
                catch (Exception ex)
                {
                    retry = true;
                    OnProcessException(new InvalidOperationException(string.Format("Failed to reinitialize socket layer: {0}", ex.Message), ex));
                }
                finally
                {
                    Monitor.Exit(m_reinitializationLock);
                }

                if (retry)
                    ThreadPool.QueueUserWorkItem(ReinitializeSocketLayer);
            }
        }

        /// <summary>
        /// Resets counters related to latency calculations.
        /// </summary>
        public void ResetLatencyCounters()
        {
            m_minimumLatency = 0;
            m_maximumLatency = 0;
            m_totalLatency = 0;
            m_latencyMeasurements = 0;
        }

        /// <summary>
        /// Handles incoming commands from devices connected over the command channel.
        /// </summary>
        /// <param name="clientID">Guid of client that sent the command.</param>
        /// <param name="connectionID">Remote client connection identification (i.e., IP:Port).</param>
        /// <param name="commandBuffer">Data buffer received from connected client device.</param>
        /// <param name="length">Valid length of data within the buffer.</param>
        /// <remarks>
        /// This method should be overridden by derived classes in order to handle incoming commands,
        /// specifically handling requests for configuration frames.
        /// </remarks>
        protected virtual void DeviceCommandHandler(Guid clientID, string connectionID, byte[] commandBuffer, int length)
        {
            // This is optionally overridden to handle incoming data - such as IEEE commands
        }

        // Thread procedure used to proxy data to the user implemented device command handler
        private void DeviceCommandHandlerProc(object state)
        {
            EventArgs<Guid, byte[], int> e = state as EventArgs<Guid, byte[], int>;

            if ((object)e != null)
                DeviceCommandHandler(e.Argument1, GetConnectionID(m_commandChannel, e.Argument1), e.Argument2, e.Argument3);
        }

        /// <summary>
        /// Gets connection ID (i.e., IP:Port) for specified <paramref name="clientID"/>.
        /// </summary>
        /// <param name="server">Server connection of associated <paramref name="clientID"/>.</param>
        /// <param name="clientID">Guid of client for ID lookup.</param>
        /// <returns>Connection ID (i.e., IP:Port) for specified <paramref name="clientID"/>.</returns>
        protected virtual string GetConnectionID(IServer server, Guid clientID)
        {
            string connectionID;

            if (!m_connectionIDCache.TryGetValue(clientID, out connectionID))
            {
                // Attempt to lookup remote connection identification for logging purposes
                try
                {
                    IPEndPoint remoteEndPoint = null;
                    TcpServer commandChannel = server as TcpServer;
                    TransportProvider<Socket> tcpClient;
                    TransportProvider<EndPoint> udpClient;

                    if ((object)commandChannel != null)
                    {
                        if (commandChannel.TryGetClient(clientID, out tcpClient))
                            remoteEndPoint = tcpClient.Provider.RemoteEndPoint as IPEndPoint;
                    }
                    else
                    {
                        UdpServer dataChannel = server as UdpServer;

                        if ((object)dataChannel != null && dataChannel.TryGetClient(clientID, out udpClient))
                            remoteEndPoint = udpClient.Provider as IPEndPoint;
                    }

                    if ((object)remoteEndPoint != null)
                    {
                        if (remoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                            connectionID = "[" + remoteEndPoint.Address + "]:" + remoteEndPoint.Port;
                        else
                            connectionID = remoteEndPoint.Address + ":" + remoteEndPoint.Port;

                        // Cache value for future lookup
                        m_connectionIDCache.TryAdd(clientID, connectionID);
                        ThreadPool.QueueUserWorkItem(LookupHostName, new Tuple<Guid, string, IPEndPoint>(clientID, connectionID, remoteEndPoint));
                    }
                }
                catch (Exception ex)
                {
                    OnProcessException(new InvalidOperationException("Failed to lookup remote end-point connection information for client data transmission due to exception: " + ex.Message, ex));
                }

                if (string.IsNullOrEmpty(connectionID))
                    connectionID = "unavailable";
            }

            return connectionID;
        }

        private void LookupHostName(object state)
        {
            try
            {
                Tuple<Guid, string, IPEndPoint> parameters = (Tuple<Guid, string, IPEndPoint>)state;
                IPHostEntry ipHost = Dns.GetHostEntry(parameters.Item3.Address);

                if (!string.IsNullOrWhiteSpace(ipHost.HostName))
                    m_connectionIDCache[parameters.Item1] = ipHost.HostName + " (" + parameters.Item2 + ")";
            }
            catch
            {
                // Just ignoring possible DNS lookup failures...
            }
        }

        /// <summary>
        /// Creates a new protocol specific <see cref="IConfigurationFrame"/> based on provided protocol independent <paramref name="baseConfigurationFrame"/>.
        /// </summary>
        /// <param name="baseConfigurationFrame">Protocol independent <see cref="ConfigurationFrame"/>.</param>
        /// <returns>A new protocol specific <see cref="IConfigurationFrame"/>.</returns>
        /// <remarks>
        /// Derived classes should notify consumers of change in configuration if system is active when
        /// new configuration frame is created if outgoing protocol allows such a notfication.
        /// </remarks>
        protected abstract IConfigurationFrame CreateNewConfigurationFrame(ConfigurationFrame baseConfigurationFrame);

        /// <summary>
        /// Serialize configuration frame to cache folder for later use (if needed).
        /// </summary>
        /// <param name="configurationFrame">New <see cref="IConfigurationFrame"/> to cache.</param>
        /// <param name="name">Name to use when caching the <paramref name="configurationFrame"/>.</param>
        /// <remarks>
        /// Derived concentrators can call this method to manually serialize their protocol specific
        /// configuration frames. Note that after initial call to <see cref="CreateNewConfigurationFrame"/>
        /// this method will be call automatically.
        /// </remarks>
        protected void CacheConfigurationFrame(IConfigurationFrame configurationFrame, string name)
        {
            // Cache configuration frame for reference
            OnStatusMessage("Caching configuration frame...");

            try
            {
                // Cache configuration on an independent thread in case this takes some time
                GSF.PhasorProtocols.Anonymous.ConfigurationFrame.Cache(configurationFrame, OnProcessException, name);
            }
            catch (Exception ex)
            {
                // Process exception for logging
                OnProcessException(new InvalidOperationException("Failed to queue caching of config frame due to exception: " + ex.Message, ex));
            }
        }

        /// <summary>
        /// Get signal reference for specified <see cref="SignalKind"/>.
        /// </summary>
        /// <param name="type"><see cref="SignalKind"/> to request signal reference for.</param>
        /// <returns>Signal reference of given <see cref="SignalKind"/>.</returns>
        public string GetSignalReference(SignalKind type)
        {
            // We cache non-indexed signal reference strings so they don't need to be generated at each mapping call.
            string[] references;

            // Look up synonym in dictionary based on signal type, if found return single element
            if (m_generatedSignalReferenceCache.TryGetValue(type, out references))
                return references[0];

            // Create a new signal reference array (for single element)
            references = new string[1];

            // Create and cache new non-indexed signal reference
            references[0] = SignalReference.ToString(Name + "!OS", type);

            // Cache generated signal synonym
            m_generatedSignalReferenceCache.TryAdd(type, references);

            return references[0];
        }

        /// <summary>
        /// Get signal reference for specified <see cref="SignalKind"/> and <paramref name="index"/>.
        /// </summary>
        /// <param name="type"><see cref="SignalKind"/> to request signal reference for.</param>
        /// <param name="index">Index <see cref="SignalKind"/> to request signal reference for.</param>
        /// <param name="count">Number of signals defined for this <see cref="SignalKind"/>.</param>
        /// <returns>Signal reference of given <see cref="SignalKind"/> and <paramref name="index"/>.</returns>
        public string GetSignalReference(SignalKind type, int index, int count)
        {
            // We cache indexed signal reference strings so they don't need to be generated at each mapping call.
            // For speed purposes we intentionally do not validate that signalIndex falls within signalCount, be
            // sure calling procedures are very careful with parameters...
            string[] references;

            // Look up synonym in dictionary based on signal type
            if (m_generatedSignalReferenceCache.TryGetValue(type, out references))
            {
                // Verify signal count has not changed (we may have received new configuration from device)
                if (count == references.Length)
                {
                    // Create and cache new signal reference if it doesn't exist
                    if (references[index] == null)
                        references[index] = SignalReference.ToString(Name + "!OS", type, index + 1);

                    return references[index];
                }
            }

            // Create a new indexed signal reference array
            references = new string[count];

            // Create and cache new signal reference
            references[index] = SignalReference.ToString(Name + "!OS", type, index + 1);

            // Cache generated signal synonym array
            m_generatedSignalReferenceCache.TryAdd(type, references);

            return references[index];
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
            if (m_hashCode == 0)
                m_hashCode = Guid.NewGuid().GetHashCode();

            return m_hashCode;
        }

        // Updates the measurements per second counters after receiving another set of measurements.
        private void UpdateMeasurementsPerSecond(int measurementCount)
        {
            long secondsSinceEpoch = DateTime.UtcNow.Ticks / Ticks.PerSecond;

            if (secondsSinceEpoch > m_lastSecondsSinceEpoch)
            {
                if (m_measurementsInSecond < m_minimumMeasurementsPerSecond || m_minimumMeasurementsPerSecond == 0L)
                    m_minimumMeasurementsPerSecond = m_measurementsInSecond;

                if (m_measurementsInSecond > m_maximumMeasurementsPerSecond || m_maximumMeasurementsPerSecond == 0L)
                    m_maximumMeasurementsPerSecond = m_measurementsInSecond;

                m_totalMeasurementsPerSecond += m_measurementsInSecond;
                m_measurementsPerSecondCount++;
                m_measurementsInSecond = 0L;

                m_lastSecondsSinceEpoch = secondsSinceEpoch;
            }

            m_measurementsInSecond += measurementCount;
        }

        // Resets the measurements per second counters after reading the values from the last calculation interval.
        private void ResetMeasurementsPerSecondCounters()
        {
            m_minimumMeasurementsPerSecond = 0L;
            m_maximumMeasurementsPerSecond = 0L;
            m_totalMeasurementsPerSecond = 0L;
            m_measurementsPerSecondCount = 0L;
        }

        #region [ Data Channel Event Handlers ]

        private void m_dataChannel_ClientConnectingException(object sender, EventArgs<Exception> e)
        {
            Exception ex = e.Argument;
            OnProcessException(new InvalidOperationException(string.Format("Exception occurred while client attempting to connect to data channel: {0}", ex.Message), ex));
        }

        private void m_dataChannel_ReceiveClientDataComplete(object sender, EventArgs<Guid, byte[], int> e)
        {
            // Queue up derived class device command handling on a different thread since this will
            // often engage sending data back on the same command channel and we want this async
            // thread to complete gracefully...
            if ((object)m_commandChannel == null)
                ThreadPool.QueueUserWorkItem(DeviceCommandHandlerProc, e);
        }

        private void m_dataChannel_SendClientDataException(object sender, EventArgs<Guid, Exception> e)
        {
            Exception ex = e.Argument2;

            if (ex is SocketException)
            {
                // Restart connection if a socket exception occurs
                OnProcessException(new InvalidOperationException(string.Format("Socket exception occurred on the data channel while attempting to send client data to \"{0}\": {1}", GetConnectionID(m_dataChannel, e.Argument1), ex.Message), ex));
                ThreadPool.QueueUserWorkItem(ReinitializeSocketLayer);
            }
            else
            {
                OnProcessException(new InvalidOperationException(string.Format("Data channel exception occurred while sending client data to \"{0}\": {1}", GetConnectionID(m_dataChannel, e.Argument1), ex.Message), ex));
            }
        }

        private void m_dataChannel_ServerStarted(object sender, EventArgs e)
        {
            // Start concentration engine
            if (m_autoStartDataChannel)
                base.Start();

            m_activeConnections++;
            OnStatusMessage("Data channel started.");
        }

        private void m_dataChannel_ServerStopped(object sender, EventArgs e)
        {
            m_activeConnections--;
            OnStatusMessage("Data channel stopped.");
        }

        #endregion

        #region [ Command Channel Event Handlers ]

        private void m_commandChannel_ClientConnected(object sender, EventArgs<Guid> e)
        {
            OnStatusMessage("Client \"{0}\" connected to command channel.", GetConnectionID(m_commandChannel, e.Argument));
        }

        private void m_commandChannel_ClientDisconnected(object sender, EventArgs<Guid> e)
        {
            Guid clientID = e.Argument;
            string connectionID;

            OnStatusMessage("Client \"{0}\" disconnected from command channel.", GetConnectionID(m_commandChannel, clientID));

            m_connectionIDCache.TryRemove(clientID, out connectionID);
        }

        private void m_commandChannel_ClientConnectingException(object sender, EventArgs<Exception> e)
        {
            Exception ex = e.Argument;
            OnProcessException(new InvalidOperationException(string.Format("Socket exception occurred while client attempting to connect to command channel: {0}", ex.Message), ex));
        }

        private void m_commandChannel_ReceiveClientDataComplete(object sender, EventArgs<Guid, byte[], int> e)
        {
            // Queue up derived class device command handling on a different thread since this will
            // often engage sending data back on the same command channel and we want this async
            // thread to complete gracefully...
            ThreadPool.QueueUserWorkItem(DeviceCommandHandlerProc, e);
        }

        private void m_commandChannel_SendClientDataException(object sender, EventArgs<Guid, Exception> e)
        {
            Exception ex = e.Argument2;

            if (ex is SocketException)
            {
                // Restart connection if a socket exception occurs
                OnProcessException(new InvalidOperationException(string.Format("Socket exception occurred on the command channel while attempting to send client data to \"{0}\": {1}", GetConnectionID(m_dataChannel, e.Argument1), ex.Message), ex));
                ThreadPool.QueueUserWorkItem(ReinitializeSocketLayer);
            }
            else
            {
                OnProcessException(new InvalidOperationException(string.Format("Command channel exception occurred while sending client data to \"{0}\": {1}", GetConnectionID(m_commandChannel, e.Argument1), ex.Message), ex));
            }
        }

        private void m_commandChannel_ServerStarted(object sender, EventArgs e)
        {
            OnStatusMessage("Command channel started.");
            m_activeConnections++;
        }

        private void m_commandChannel_ServerStopped(object sender, EventArgs e)
        {
            m_activeConnections--;

            if (Enabled)
            {
                OnStatusMessage("Command channel was unexpectedly terminated, restarting...");

                // We must wait for command channel to completely shutdown before trying to restart...
                if (m_commandChannelRestartTimer != null)
                    m_commandChannelRestartTimer.Start();
            }
            else
                OnStatusMessage("Command channel stopped.");
        }

        private void m_commandChannelRestartTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (m_commandChannel != null)
            {
                try
                {
                    // After a short delay, we try to restart the command channel
                    m_commandChannel.Start();
                }
                catch (Exception ex)
                {
                    OnProcessException(new InvalidOperationException("Failed to restart command channel: " + ex.Message, ex));
                }
            }
        }

        #endregion

        #endregion
    }
}