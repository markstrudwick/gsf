//*******************************************************************************************************
//  TimeTagBase.cs
//  Copyright © 2008 - TVA, all rights reserved - Gbtc
//
//  Build Environment: C#, Visual Studio 2008
//  Primary Developer: James R Carroll
//      Office: PSO TRAN & REL, CHATTANOOGA - MR 2W-C
//       Phone: 423/751-2827
//       Email: jrcarrol@tva.gov
//
//  Code Modification History:
//  -----------------------------------------------------------------------------------------------------
//  07/12/2006 - J. Ritchie Carroll
//       Generated original version of source code.
//  11/03/2006 - J. Ritchie Carroll
//       Updated base time comparison to use .NET date time, since compared time-tags may not
//       have the same base time ticks.
//  09/07/2007 - Darrell Zuercher
//       Edited code comments.
//  09/12/2008 - J. Ritchie Carroll
//      Converted to C#.
//
//*******************************************************************************************************

using System;
using System.Runtime.Serialization;

namespace TVA
{
    /// <summary>Base class for alternate time tag implementations.</summary>
    public abstract class TimeTagBase : ISerializable, IComparable, IComparable<TimeTagBase>, IComparable<DateTime>, IEquatable<TimeTagBase>
    {
        #region [ Members ]

        // Fields
        private long m_baseDateOffsetTicks;
        private double m_seconds;

        #endregion

        #region [ Constructors ]

        protected TimeTagBase(SerializationInfo info, StreamingContext context)
        {
            // Deserializes time tag
            m_baseDateOffsetTicks = info.GetInt64("baseDateOffsetTicks");
            m_seconds = info.GetDouble("seconds");
        }

        /// <summary>Creates new time tag, given number base time (in ticks) and seconds since base time.</summary>
        /// <param name="baseDateOffsetTicks">Ticks of time tag base.</param>
        /// <param name="seconds">Number of seconds since base time.</param>
        protected TimeTagBase(long baseDateOffsetTicks, double seconds)
        {
            m_baseDateOffsetTicks = baseDateOffsetTicks;
            Value = seconds;
        }

        /// <summary>Creates new time tag, given standard .NET DateTime.</summary>
        /// <param name="baseDateOffsetTicks">Ticks of time tag base.</param>
        /// <param name="timestamp">.NET DateTime used to create time tag from.</param>
        protected TimeTagBase(long baseDateOffsetTicks, DateTime timestamp)
        {
            // Zero base 100-nanosecond ticks from 1/1/1970 and convert to seconds.
            m_baseDateOffsetTicks = baseDateOffsetTicks;
            Value = Ticks.ToSeconds(timestamp.Ticks - m_baseDateOffsetTicks);
        }

        #endregion

        #region [ Properties ]

        /// <summary>Gets or sets number of seconds since base time.</summary>
        public virtual double Value
        {
            get
            {
                return m_seconds;
            }
            set
            {
                m_seconds = value;
                if (m_seconds < 0) m_seconds = 0;
            }
        }

        /// <summary>Gets ticks representing the absolute minimum time of this time tag implementation.</summary>
        public virtual long BaseDateOffsetTicks
        {
            get
            {
                return m_baseDateOffsetTicks;
            }
        }

        #endregion

        #region [ Methods ]

        /// <summary>Returns standard .NET DateTime representation for time tag.</summary>
        public virtual DateTime ToDateTime()
        {
            // Converts m_seconds to 100-nanosecond ticks and add the base time offset.
            return new DateTime(Seconds.ToTicks(m_seconds) + m_baseDateOffsetTicks);
        }

        /// <summary>Returns basic textual representation for time tag.</summary>
        /// <remarks>Format is "yyyy-MM-dd HH:mm:ss.fff" so that textual representation can be sorted in the
        /// correct chronological order.</remarks>
        public override string ToString()
        {
            return ToDateTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        /// <summary>Compares this time tag to another one.</summary>
        public int CompareTo(TimeTagBase other)
        {
            // Since compared time tags may not have the same base time, we compare using .NET date time.
            return CompareTo(other.ToDateTime());
        }

        /// <summary>Compares this time tag to a DateTime.</summary>
        public int CompareTo(DateTime other)
        {
            return ToDateTime().CompareTo(other);
        }

        public virtual int CompareTo(object obj)
        {
            TimeTagBase timetag = obj as TimeTagBase;

            if (timetag != null) return CompareTo(timetag);

            if (obj is DateTime) return CompareTo((DateTime)obj);

            throw new ArgumentException("TimeTag can only be compared with other TimeTags or DateTimes...");
        }

        public override bool Equals(object obj)
        {
            return (CompareTo(obj) == 0);
        }

        public bool Equals(TimeTagBase other)
        {
            return (CompareTo(other) == 0);
        }

        public override int GetHashCode()
        {
            return (int)(m_seconds * 1000);
        }

        public virtual void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
        {
            // Serializes time tag.
            info.AddValue("baseDateOffsetTicks", m_baseDateOffsetTicks);
            info.AddValue("seconds", m_seconds);
        }

        #endregion
    }
}