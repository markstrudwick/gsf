//******************************************************************************************************
//  CompactMeasurementParser.cpp - Gbtc
//
//  Copyright � 2010, Grid Protection Alliance.  All Rights Reserved.
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
//  03/09/2012 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

#include "CompactMeasurementParser.h"

namespace gsfts = GSF::TimeSeries;

// Takes the 8-bit compact measurement flags and maps
// them to the full 32-bit measurement flags format.
uint32_t gsfts::Transport::CompactMeasurementParser::MapToFullFlags(uint8_t compactFlags) const
{
	unsigned int fullFlags = 0;

	if ((compactFlags & CompactMeasurementParser::CompactDataRangeFlag) > 0)
		fullFlags |= CompactMeasurementParser::DataRangeMask;

	if ((compactFlags & CompactMeasurementParser::CompactDataQualityFlag) > 0)
		fullFlags |= CompactMeasurementParser::DataQualityMask;

	if ((compactFlags & CompactMeasurementParser::CompactTimeQualityFlag) > 0)
		fullFlags |= CompactMeasurementParser::TimeQualityMask;

	if ((compactFlags & CompactMeasurementParser::CompactSystemIssueFlag) > 0)
		fullFlags |= CompactMeasurementParser::SystemIssueMask;

	if ((compactFlags & CompactMeasurementParser::CompactCalculatedValueFlag) > 0)
		fullFlags |= CompactMeasurementParser::CalculatedValueMask;

	if ((compactFlags & CompactMeasurementParser::CompactDiscardedValueFlag) > 0)
		fullFlags |= CompactMeasurementParser::DiscardedValueMask;

	return fullFlags;
}

// Gets the byte length of measurements parsed by this parser.
std::size_t gsfts::Transport::CompactMeasurementParser::GetMeasurementByteLength(bool usingBaseTimeOffset) const
{
	std::size_t byteLength = 7;

	if (m_includeTime)
	{
		if (!usingBaseTimeOffset)
			byteLength += 8;
		else if (!m_useMillisecondResolution)
			byteLength += 4;
		else
			byteLength += 2;
	}

	return byteLength;
}

// Attempts to parse a measurement from the buffer. Return value of false indicates
// that there is not enough data to parse the measurement. Offset and length will be
// updated by this method to indicate how many bytes were used when parsing.
bool gsfts::Transport::CompactMeasurementParser::TryParseMeasurement(uint8_t* buffer, std::size_t& offset, std::size_t& length)
{
	uint8_t compactFlags;
	uint16_t signalIndex;
	Guid signalID;
	std::string measurementSource;
	uint32_t measurementID;
	float32_t measurementValue;
	int64_t timestamp = 0;

	bool usingBaseTimeOffset;
	std::size_t timeIndex;

	std::size_t end = offset + length;

	// Ensure that we at least have enough
	// data to read the compact state flags
	if (length < 1)
		return false;

	// Read the compact state flags to determine
	// the size of the measurement being parsed
	compactFlags = buffer[offset] & 0xFF;
	usingBaseTimeOffset = (compactFlags & CompactBaseTimeOffsetFlag);
	timeIndex = (compactFlags & CompactTimeIndexFlag) ? 1 : 0;

	// If we are using base time offsets, ensure that it is defined
	if (usingBaseTimeOffset && (m_baseTimeOffsets == 0 || m_baseTimeOffsets[timeIndex] == 0))
		return false;

	// Ensure that we have enough data to read the rest of the measurement
	if (length < GetMeasurementByteLength(usingBaseTimeOffset))
		return false;

	// Read the signal index from the buffer
	signalIndex = m_endianConverter.ConvertBigEndian<uint16_t>(*(uint16_t*)(buffer + offset + 1));

	// If the signal index is not found in the cache, we cannot parse the measurement
	if (!m_signalIndexCache.Contains(signalIndex))
		return false;

	// Now that we've validated our failure conditions,
	// we can safely start advancing the offset
	m_signalIndexCache.GetMeasurementKey(signalIndex, signalID, measurementSource, measurementID);
	offset += 3;

	// Read the measurement value from the buffer
	measurementValue = m_endianConverter.ConvertBigEndian<float32_t>(*(float32_t*)(buffer + offset));
	offset += 4;

	if (m_includeTime)
	{
		if (!usingBaseTimeOffset)
		{
			// Read full 8-byte timestamp from the buffer
			timestamp = m_endianConverter.ConvertBigEndian<int64_t>(*(int64_t*)(buffer + offset));
			offset += 8;
		}
		else if (!m_useMillisecondResolution)
		{
			// Read 4-byte offset from the buffer and apply the appropriate base time offset
			timestamp = m_endianConverter.ConvertBigEndian<uint32_t>(*(uint32_t*)(buffer + offset));
			timestamp += m_baseTimeOffsets[timeIndex];
			offset += 4;
		}
		else
		{
			// Read 2-byte offset from the buffer, convert from milliseconds to ticks, and apply the appropriate base time offset
			timestamp = m_endianConverter.ConvertBigEndian<uint16_t>(*(uint16_t*)(buffer + offset));
			timestamp *= 10000;
			timestamp += m_baseTimeOffsets[timeIndex];
			offset += 2;
		}
	}

	length = end - offset;

	m_parsedMeasurement.Flags = MapToFullFlags(compactFlags);
	m_parsedMeasurement.SignalID = signalID;
	m_parsedMeasurement.Source = measurementSource;
	m_parsedMeasurement.ID = measurementID;
	m_parsedMeasurement.Value = measurementValue;
	m_parsedMeasurement.Timestamp = timestamp;

	return true;
}