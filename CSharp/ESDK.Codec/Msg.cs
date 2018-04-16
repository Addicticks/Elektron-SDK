﻿/*|-----------------------------------------------------------------------------
 *|            This source code is provided under the Apache 2.0 license      --
 *|  and is provided AS IS with no warranty or guarantee of fit for purpose.  --
 *|                See the project's LICENSE.md for details.                  --
 *|           Copyright Thomson Reuters 2018. All rights reserved.            --
 *|-----------------------------------------------------------------------------
 */

using ThomsonReuters.Eta.Common;

namespace ThomsonReuters.Eta.Codec
{

    /// <summary>
    /// This class provides the implementation for the superset of message classes
    /// (e.g. RequestMsg, RefreshMsg, etc.).
    /// Representing the superset of message types in a single class affords users the ability
    /// to maintain a single pool of messages that can be re-used across message types
    /// Users cast to the appropriate message-class specific interface.
    /// For example, an instance of this class may initially represent a
    /// request message, and later, a response message.
    /// </summary>
    public sealed class Msg : IMsg, IAckMsg, ICloseMsg, IGenericMsg, IPostMsg, IRefreshMsg, IRequestMsg, IStatusMsg, IUpdateMsg
	{
		private const int GENERAL_PURPOSE_LONGS = 2;
		private const int GENERAL_PURPOSE_INTS = 4;
		private const int GENERAL_PURPOSE_BUFFERS = 2;
		private const int GENERAL_PURPOSE_QOS = 2;

        /// <summary>
        /// Creates a <see cref="Msg"/> that may be cast to any message class defined by
        /// <see cref="MsgClasses"/> (e.g. <see cref="IAckMsg"/>, <see cref="ICloseMsg"/>,
        /// <see cref="IGenericMsg"/>, <see cref="IPostMsg"/>, <see cref="IRefreshMsg"/>,
        /// <see cref="IRequestMsg"/>, <see cref="IStatusMsg"/>, <see cref="IUpdateMsg"/>)
        /// </summary>
        /// <returns> Msg object
        /// </returns>
        /// <seealso cref="MsgClasses"/>
        /// <seealso cref="IAckMsg"/>
        /// <seealso cref="ICloseMsg"/>
        /// <seealso cref="IGenericMsg"/>
        /// <seealso cref="IPostMsg"/>
        /// <seealso cref="IRefreshMsg"/>
        /// <seealso cref="IRequestMsg"/>
        /// <seealso cref="IStatusMsg"/>
        /// <seealso cref="IUpdateMsg"/>
        public Msg()
        {
            ContainerType = DataTypes.CONTAINER_TYPE_MIN;
        }

		/* To make efficient use of memory and let a single MsgImpl class
		 * represent any message class defined by MsgClasses,
		 * we "simulate a C++ union" by (re)using general-purpose fields that are
		 * sufficient to provide storage for all the message classes.
		 * 
		 * This (private) class tells us where message-class-specific values will be stored.
		 */
		private class MsgFields
		{
			public class Common
			{
				public class Long
				{
					internal const int SEQ_NUM = 0;
				}

				public class Int
				{
                    internal const int FLAGS = 0;
					internal const int PART_NUM = 1;
				}

				public class Buffer
				{
                    internal const int PERM_DATA = 0;
					internal const int GROUP_ID = 1;
				}

				public class Qos
				{
                    internal const int QOS = 0;
				}
			}

			public class Ack
			{
                public class Long
				{
                    internal const int ACK_ID = 1;
				}

				public class Int
				{
                    internal const int NAK_CODE = 2;
				}

				public class Buffer
				{
                    internal const int TEXT = 0;
				}
			}

			public class Generic
			{
                public class Long
				{
                    internal const int SECONDARY_SEQ_NUM = 1;
				}
			}

			public class Post
			{
                public class Long
				{

                    internal const int POST_ID = 1;
				}

				public class Int
				{
                    internal const int POST_USER_RIGHTS = 3;
				}
			}

			public class Request
			{
                public class Qos
				{
                    internal const int WORST_QOS = 1;
				}
			}

			public class Update
			{
                public class Int
				{
                    internal const int UPDATE_TYPE = 1;
					internal const int CONFLATION_COUNT = 2;
					internal const int CONFLATION_TIME = 3;
				}
			}
		}

		// the following fields are common to all the message types
		private readonly MsgKey _msgKey = new MsgKey();
		private readonly Buffer _extendedHeader = new Buffer();
		private readonly Buffer _encodedDataBody = new Buffer();
		private readonly Buffer _encodedMsgBuffer = new Buffer();

		// Through the use of the following general-purpose fields, we attempt
		// to "simulate" a C++ union so we can implement storage for the superset
		// of message types while using as little memory as possible.
		private long[] _generalLong = new long[GENERAL_PURPOSE_LONGS];
		private int[] _generalInt = new int[GENERAL_PURPOSE_INTS];
		private Buffer[] _generalBuffer = new Buffer[GENERAL_PURPOSE_BUFFERS];
		private readonly PostUserInfo _generalPostUserInfo = new PostUserInfo();
		private State _generalState;
		private Qos[] _generalQos = new Qos[GENERAL_PURPOSE_QOS];
		private Priority _generalPriority;

		// flags to track if key and extended header space to be reserved
		internal bool _keyReserved;
		internal bool _extendedHeaderReserved;

        /// <summary>
        /// Gets the <see cref="ThomsonReuters.Eta.Codec.IMsgKey"/>
        /// </summary>
		public IMsgKey MsgKey
		{
            get
            {
                switch (MsgClass)
                {
                    case MsgClasses.UPDATE:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_MSG_KEY) > 0 ? _msgKey : null);
                    case MsgClasses.REFRESH:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_MSG_KEY) > 0 ? _msgKey : null);
                    case MsgClasses.STATUS:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_MSG_KEY) > 0 ? _msgKey : null);
                    case MsgClasses.GENERIC:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_MSG_KEY) > 0 ? _msgKey : null);
                    case MsgClasses.POST:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_MSG_KEY) > 0 ? _msgKey : null);
                    case MsgClasses.ACK:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_MSG_KEY) > 0 ? _msgKey : null);
                    case MsgClasses.REQUEST: // request message always has message key
                        return _msgKey;
                    default: // all other messages don't have message key
                        return null;
                }
            }
		}

        /// <summary>
        ///  Encode a ETA Message. Encodes the key into buffer, all the data is
        /// passed in.
        /// Typical use:
        /// 1. Set Msg structure members.
        /// 2. Encode key(s) in separate buffer(s) and set Msg key members appropriately.
        /// 3. Encode message body in separate buffer and set Msg encodedDataBody member appropriately.
        /// 4. Call Msg.Encode().
        /// </summary>
        /// <param name="iter">Encoding iterator</param>
        /// <returns><see cref="CodecReturnCode"/></returns>
        /// <seealso cref="ThomsonReuters.Eta.Codec.MsgKey"/>
        /// <seealso cref="EncodeIterator"/>
		public CodecReturnCode Encode(EncodeIterator iter)
		{
			return Encoders.EncodeMsg(iter, this);
        }

        /// <summary>
        /// Initiate encoding of a ETA Message. Initiates encoding of a message.
        /// Typical use:
        /// 1. Call Msg.EncodeInit().
        /// 2. Encode the key contents.
        /// 3. Call Msg.EncodeKeyAttribComplete().
        /// 4. Encode the message data body as needed.
        /// 5. Call Msg.EncodeComplete().
        /// </summary>
        /// <param name="iter">Encoding iterator</param>
        /// <param name="dataMaxSize">Max encoding size of the data, set to 0 if unknown</param>
        /// <returns><see cref="CodecReturnCode"/></returns>
        /// <seealso cref="ThomsonReuters.Eta.Codec.MsgKey"/>
        /// <seealso cref="EncodeIterator"/>
        public CodecReturnCode EncodeInit(EncodeIterator iter, int dataMaxSize)
		{
			return Encoders.EncodeMsgInit(iter, this, dataMaxSize);
        }

        /// <summary>
        /// Complete encoding of a ETA Message. Complete encoding of a message.
        /// Typical use:
        /// 1. Call Msg.EncodeInit().
        /// 2. Encode the key contents.
        /// 3. Call Msg.EncodeKeyAttribComplete().
        /// 4. Encode the message data body as needed.
        /// 5. Call Msg.EncodeComplete().
        /// Note, step 2 and 3 are optional, instead the application can set the
        /// respective key members of the <see cref="Msg"/>
        /// structure using pre-encoded buffers.
        /// </summary>
        /// <param name="iter">iter Encoding iterator</param>
        /// <param name="success">If true - successfully complete the key,
        ///                 if false - remove the key from the buffer.</param>
        /// <returns><see cref="CodecReturnCode"/></returns>
        /// <seealso cref="ThomsonReuters.Eta.Codec.MsgKey"/>
        /// <seealso cref="EncodeIterator"/>
        public CodecReturnCode EncodeComplete(EncodeIterator iter, bool success)
		{
			return Encoders.EncodeMsgComplete(iter, success);
        }

        /// <summary>
        /// Complete Encoding of msgKey.opaque data.
        /// Typically used when user calls Msg.encodeInit using a msgKey indicating that
        /// the msgKey.EncodedAttrib should be encoded and the encodedAttrib.Length and
        /// encodedAttrib.data are not populated with pre-encoded
        /// msgKey.EncodedAttrib data. 
        /// Msg.EncodeInit will return <see cref="CodecReturnCode.ENCODE_MSG_KEY_ATTRIB"/>, the user will
        /// invoke the container encoders for their msgKey.encodedAttrib, and after it is complete call
        /// encodeKeyAttribComplete.
        /// </summary>
        /// <param name="iter">iter Encoding iterator</param>
        /// <param name="success">success If true, finish encoding - else rollback</param>
        /// <returns><see cref="CodecReturnCode"/></returns>
        /// <seealso cref="EncodeIterator"/>
        public CodecReturnCode EncodeKeyAttribComplete(EncodeIterator iter, bool success)
		{
			return Encoders.EncodeMsgKeyAttribComplete(iter, success);
        }

        /// <summary>
        /// Complete encoding of an Extended Header. Encodes the extended header, the data is passed in.
        /// Typical use:
        /// 1. Call Msg.EncodeInit().
        /// 2. Call extended header encoding methods.
        /// 3. Call Msg.EncodeExtendedHeaderComplete().
        /// </summary>
        /// <param name="iter">Encoding iterator</param>
        /// <param name="success">If true, finish encoding - else rollback</param>
        /// <returns><see cref="CodecReturnCode"/></returns>
        /// <seealso cref="EncodeIterator"/>
		public CodecReturnCode EncodeExtendedHeaderComplete(EncodeIterator iter, bool success)
		{
			return Encoders.EncodeExtendedHeaderComplete(iter, success);
        }

        /// <summary>
        /// Class of this message (Update, Refresh, Status, etc).
        /// Populated from <see cref="MsgClasses"/> definition.
        /// </summary>
		public int MsgClass { get; set; }

        /// <summary>
        /// Gets or sets domain Type of this message, corresponds to a domain model definition
        /// (values less than 128 are Thomson Reuters defined domain models, values
        /// between 128 - 255 are user defined domain models).
        /// Must be in the range of 0 - 255.
        /// </summary>
		public int DomainType { get; set; }

        /// <summary>
        /// Gets or sets container type that is held in the EncodedDataBody. ContainerType must be
        /// from the <see cref="DataTypes"/>
        /// enumeration in the range
        /// <see cref="DataTypes.CONTAINER_TYPE_MIN"/> to 255.
        /// </summary>
		public int ContainerType { get; set; }

        /// <summary>
        /// Gets or sets unique identifier associated with all messages flowing within a stream
        /// (positive values indicate a consumer instantiated stream, negative values
        /// indicate a provider instantiated stream often associated with non-interactive providers). 
        /// Must be in the range of -2147483648 - 2147483647.
        /// </summary>
		public int StreamId { get; set; }

        /// <summary>
        /// Gets or sets extended header information.
        /// </summary>
		public Buffer ExtendedHeader
		{
            get
            {
                switch (MsgClass)
                {
                    case MsgClasses.UPDATE:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_EXTENDED_HEADER) > 0 ? _extendedHeader : null);
                    case MsgClasses.REFRESH:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_EXTENDED_HEADER) > 0 ? _extendedHeader : null);
                    case MsgClasses.STATUS:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_EXTENDED_HEADER) > 0 ? _extendedHeader : null);
                    case MsgClasses.GENERIC:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_EXTENDED_HEADER) > 0 ? _extendedHeader : null);
                    case MsgClasses.CLOSE:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & CloseMsgFlags.HAS_EXTENDED_HEADER) > 0 ? _extendedHeader : null);
                    case MsgClasses.REQUEST:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_EXTENDED_HEADER) > 0 ? _extendedHeader : null);
                    case MsgClasses.POST:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_EXTENDED_HEADER) > 0 ? _extendedHeader : null);
                    case MsgClasses.ACK:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_EXTENDED_HEADER) > 0 ? _extendedHeader : null);
                    default:
                        return null;
                }
            }

            set
            {
                (_extendedHeader).CopyReferences(value);
            }
		}

        /// <summary>
        /// Gets or sets encoded payload contents of the message
        /// </summary>
		public Buffer EncodedDataBody
		{
            get
            {
                return _encodedDataBody;
            }

            set
            {
                (_encodedDataBody).CopyReferences(value);
            }
		}

        /// <summary>
        /// Gets <see cref="Buffer"/> that contains the entire encoded message, typically only populated during decode.
        /// </summary>
		public Buffer EncodedMsgBuffer
		{
            get
            {
                return _encodedMsgBuffer;
            }
		}

        /// <summary>
        /// Decodes a message.
        /// </summary>
        /// <param name="iter">Decode iterator</param>
        /// <returns><see cref="CodecReturnCode"/></returns>
		public CodecReturnCode Decode(DecodeIterator iter)
		{
			return Decoders.DecodeMsg(iter, this);
        }

        /// <summary>
        /// Set iterator to decode a message's key attribute buffer
        /// After calling Eecode(), this method can be used on the iterator to set
        /// it for decoding the key's EncodedAttrib buffer instead of the
        /// encodedDataBody.When the decoding of the EncodedAttrib buffer is
        /// complete, it will be ready to decode the EncodedDataBody as normal.
        /// </summary>
        /// <param name="iter">Iterator to set</param>
        /// <param name="key">Key from the decoded message</param>
        /// <returns><see cref="CodecReturnCode"/></returns>
        public CodecReturnCode DecodeKeyAttrib(DecodeIterator iter, IMsgKey key)
		{
			return Decoders.DecodeMsgKeyAttrib(iter, key);
        }

        /// <summary>
        /// Copy <see cref="Msg"/>.
        /// Performs a deep copy of a <see cref="Msg"/>
        /// structure.Expects all memory to be
        /// owned and managed by user.If the memory for the buffers (i.e.name, attrib, ect.)
        /// are not provided, they will be created.
        /// </summary>
        /// <param name="destMsg">to copy Msg structure into.</param>
        /// <param name="copyMsgFlags">controls which parameters of message are copied to destination message</param>
        /// <returns><see cref="CodecReturnCode.SUCCESS"/>, if the message is copied successfully,
        ///         <see cref="CodecReturnCode.FAILURE"/> if the source message is invalid
        ///         <see cref="CodecReturnCode.BUFFER_TOO_SMALL"/> if the buffer provided is too small</returns>
        /// <seealso cref="CopyMsgFlags"/>
        public CodecReturnCode Copy(IMsg destMsg, int copyMsgFlags)
		{
            if(destMsg is null )
                return CodecReturnCode.FAILURE;

            if (!ValidateMsg())
			{
				return CodecReturnCode.FAILURE;
			}

			destMsg.Clear();
			destMsg.MsgClass = MsgClass;
			destMsg.DomainType = DomainType;
			destMsg.ContainerType = ContainerType;
			destMsg.StreamId = StreamId;

			for (int i = 0; i < GENERAL_PURPOSE_LONGS; i++)
			{
				((Msg)destMsg)._generalLong[i] = _generalLong[i];
			}
			for (int i = 0; i < GENERAL_PURPOSE_INTS; i++)
			{
				((Msg)destMsg)._generalInt[i] = _generalInt[i];
			}

			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					IUpdateMsg updMsg = (IUpdateMsg)destMsg;
					return CopyUpdateMsg(updMsg, copyMsgFlags);
				case MsgClasses.REFRESH:
					IRefreshMsg rfMsg = (IRefreshMsg)destMsg;
					return CopyRefreshMsg(rfMsg, copyMsgFlags);
				case MsgClasses.REQUEST:
					IRequestMsg rqMsg = (IRequestMsg)destMsg;
					return CopyRequestMsg(rqMsg, copyMsgFlags);
				case MsgClasses.POST:
					IPostMsg postMsg = (IPostMsg)destMsg;
					return CopyPostMsg(postMsg, copyMsgFlags);
				case MsgClasses.GENERIC:
					IGenericMsg genMsg = (IGenericMsg)destMsg;
					return CopyGenericMsg(genMsg, copyMsgFlags);
				case MsgClasses.CLOSE:
					ICloseMsg clMsg = (ICloseMsg)destMsg;
					return CopyCloseMsg(clMsg, copyMsgFlags);
				case MsgClasses.STATUS:
					IStatusMsg stMsg = (IStatusMsg)destMsg;
					return CopyStatusMsg(stMsg, copyMsgFlags);
				case MsgClasses.ACK:
					IAckMsg ackMsg = (IAckMsg)destMsg;
					return CopyAckMsg(ackMsg, copyMsgFlags);
				default:
					return CodecReturnCode.FAILURE;
			}
		}

        /// <summary>
        /// Is <see cref="Msg"/> in final state.
        /// </summary>
        /// <returns>true - if this is a final message for the request, false otherwise</returns>
		public bool FinalMsg()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					if (State.IsFinal())
					{
						return true;
					}
					if (((Flags & RefreshMsgFlags.REFRESH_COMPLETE) > 0) && (State.StreamState() == StreamStates.NON_STREAMING))
					{
						return true;
					}
					break;
				case MsgClasses.STATUS:
					if (CheckHasState() && State.IsFinal())
					{
						return true;
					}
					break;
				case MsgClasses.CLOSE:
					return true;
				default:
					return false;
			}
    
			return false;
        }

        /// <summary>
        /// Validates <see cref="Msg"/>.
        /// Validates fully populated <see cref="Msg"/>
        /// structure to ensure validity of its data members.
        /// </summary>
        /// <returns>true - if valid; false if not valid</returns>
        public bool ValidateMsg()
		{
			if (CheckHasExtendedHdr() && ((ExtendedHeader == null) || (ExtendedHeader.Length == 0)))
			{
				return false;
			}

			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
				case MsgClasses.REFRESH:
				case MsgClasses.POST:
				case MsgClasses.GENERIC:
					if (CheckHasPermData() && ((PermData == null) || (PermData.Length == 0)))
					{
						return false;
					}
					return true;
				case MsgClasses.REQUEST:
				case MsgClasses.CLOSE:
					return true;
				case MsgClasses.STATUS:
					if (CheckHasPermData() && ((PermData == null) || (PermData.Length == 0)))
					{
						return false;
					}
					if (CheckHasGroupId() && (GroupId.Length == 0))
					{
						return false;
					}
					return true;
				case MsgClasses.ACK:
					if (CheckHasText() && (Text.Length == 0))
					{
						return false;
					}
					return true;
				default:
					return false;
			}
		}

        /// <summary>
		/// Decodes to XML the data next in line to be decoded with the iterator.
		/// Data that require dictionary lookups are decoded to hexadecimal strings.
		/// </summary>
		/// <param name="iter"> Decode iterator
		/// </param>
		/// <returns> The XML representation of the container
		/// </returns>
		/// <seealso cref="DecodeIterator"/>
		public string DecodeToXml(DecodeIterator iter)
		{
			return DecodersToXML.DecodeDataTypeToXML(DataTypes.MSG, null, null, null, iter);
		}

        /// <summary>
		/// Decodes to XML the data next in line to be decoded with the iterator.
		/// </summary>
		/// <param name="iter"> Decode iterator </param>
		/// <param name="dictionary"> Data dictionary </param>
		/// <returns> The XML representation of the container
		/// </returns>
		/// <seealso cref="DecodeIterator"/>
		/// <seealso cref="DataDictionary"/>
		public string DecodeToXml(DecodeIterator iter, DataDictionary dictionary)
		{
			return DecodersToXML.DecodeDataTypeToXML(DataTypes.MSG, null, dictionary, null, iter);
        }

        /// <summary>
        /// Checks the presence of the Discardable presence flag.
        /// <para>Flags may also be bulk-get via</para>
        /// <see cref="IMsg.Flags"/>
        /// </summary>
        /// <returns>true - if exists; false if does not exist.</returns>
        public bool CheckHasExtendedHdr()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_EXTENDED_HEADER) > 0 ? true : false);
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_EXTENDED_HEADER) > 0 ? true : false);
				case MsgClasses.STATUS:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_EXTENDED_HEADER) > 0 ? true : false);
				case MsgClasses.GENERIC:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_EXTENDED_HEADER) > 0 ? true : false);
				case MsgClasses.CLOSE:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & CloseMsgFlags.HAS_EXTENDED_HEADER) > 0 ? true : false);
				case MsgClasses.REQUEST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_EXTENDED_HEADER) > 0 ? true : false);
				case MsgClasses.POST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_EXTENDED_HEADER) > 0 ? true : false);
				case MsgClasses.ACK:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_EXTENDED_HEADER) > 0 ? true : false);
				default:
					return false;
			}
        }

        /// <summary>
        ///  Applies the Extended Header presence flag.
        ///  Flags may also be bulk-set via <see cref = "IMsg.Flags" />
        /// </summary>
		public void ApplyHasExtendedHdr()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.HAS_EXTENDED_HEADER;
					break;
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.HAS_EXTENDED_HEADER;
					break;
				case MsgClasses.STATUS:
					_generalInt[MsgFields.Common.Int.FLAGS] |= StatusMsgFlags.HAS_EXTENDED_HEADER;
					break;
				case MsgClasses.GENERIC:
					_generalInt[MsgFields.Common.Int.FLAGS] |= GenericMsgFlags.HAS_EXTENDED_HEADER;
					break;
				case MsgClasses.CLOSE:
					_generalInt[MsgFields.Common.Int.FLAGS] |= CloseMsgFlags.HAS_EXTENDED_HEADER;
					break;
				case MsgClasses.REQUEST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.HAS_EXTENDED_HEADER;
					break;
				case MsgClasses.POST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= PostMsgFlags.HAS_EXTENDED_HEADER;
					break;
				case MsgClasses.ACK:
					_generalInt[MsgFields.Common.Int.FLAGS] |= AckMsgFlags.HAS_EXTENDED_HEADER;
					break;
				default:
					break;
			}
        }

        /// <summary>
        /// Gets or sets all the flags applicable to this message.
        /// Must be in the range of 0 - 32767.
        /// </summary>
		public int Flags
		{
            get
            {
                return _generalInt[MsgFields.Common.Int.FLAGS];
            }

            set
            {
                _generalInt[MsgFields.Common.Int.FLAGS] = value;
            }
        }

        /// <summary>
        /// Checks the presence of the Message Key presence flag.
        /// <para>Flags may also be bulk-get via <see cref = "IMsg.Flags" /></para>
        /// </summary>
        /// <returns> true - if exists; false if does not exist.</returns>
        public bool CheckHasMsgKey()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_MSG_KEY) > 0 ? true : false);
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_MSG_KEY) > 0 ? true : false);
				case MsgClasses.STATUS:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_MSG_KEY) > 0 ? true : false);
				case MsgClasses.GENERIC:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_MSG_KEY) > 0 ? true : false);
				case MsgClasses.POST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_MSG_KEY) > 0 ? true : false);
				case MsgClasses.ACK:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_MSG_KEY) > 0 ? true : false);
				default:
					return false;
			}
        }

        /// <summary>
        /// Applies the Message Key presence flag.
        /// <para>Flags may also be bulk-set via <see cref = "IMsg.Flags"/></para>
        /// </summary>
		public void ApplyHasMsgKey()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.HAS_MSG_KEY;
					break;
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.HAS_MSG_KEY;
					break;
				case MsgClasses.STATUS:
					_generalInt[MsgFields.Common.Int.FLAGS] |= StatusMsgFlags.HAS_MSG_KEY;
					break;
				case MsgClasses.GENERIC:
					_generalInt[MsgFields.Common.Int.FLAGS] |= GenericMsgFlags.HAS_MSG_KEY;
					break;
				case MsgClasses.POST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= PostMsgFlags.HAS_MSG_KEY;
					break;
				case MsgClasses.ACK:
					_generalInt[MsgFields.Common.Int.FLAGS] |= AckMsgFlags.HAS_MSG_KEY;
					break;
				default:
					break;
			}
        }

        /// <summary>
        /// Checks the presence of the Sequence Number presence flag.
        /// <para>Flags may also be bulk-get via <see cref = "IMsg.Flags"/></para>
        /// </summary>
        /// <returns>true - if exists; false if does not exist.</returns>
        public bool CheckHasSeqNum()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_SEQ_NUM) > 0 ? true : false);
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_SEQ_NUM) > 0 ? true : false);
				case MsgClasses.GENERIC:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_SEQ_NUM) > 0 ? true : false);
				case MsgClasses.POST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_SEQ_NUM) > 0 ? true : false);
				case MsgClasses.ACK:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_SEQ_NUM) > 0 ? true : false);
				default:
					return false;
			}
        }

        /// <summary>
        ///  Applies the Sequence Number presence flag.
        ///  <para>Flags may also be bulk-set via <see cref = "IMsg.Flags"/></para>
        /// </summary>
		public void ApplyHasSeqNum()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.HAS_SEQ_NUM;
					break;
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.HAS_SEQ_NUM;
					break;
				case MsgClasses.STATUS:
					break;
				case MsgClasses.GENERIC:
					_generalInt[MsgFields.Common.Int.FLAGS] |= GenericMsgFlags.HAS_SEQ_NUM;
					break;
				case MsgClasses.POST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= PostMsgFlags.HAS_SEQ_NUM;
					break;
				case MsgClasses.ACK:
					_generalInt[MsgFields.Common.Int.FLAGS] |= AckMsgFlags.HAS_SEQ_NUM;
					break;
				default:
					break;
			}
        }

        /// <summary>
        /// Specifies a user-defined sequence number. To help with temporal ordering,
        /// seqNum should increase across messages, but can have gaps depending on
        /// the sequencing algorithm in use.Details about sequence number use should
        /// be defined within the domain model specification or any documentation for
        /// products which require the use of seqNum.
        /// Must be in the range of 0 - 4294967296 (2^32).
        /// </summary>
		public long SeqNum
		{
            get
            {
                switch (MsgClass)
                {
                    case MsgClasses.UPDATE:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_SEQ_NUM) > 0 ? _generalLong[MsgFields.Common.Long.SEQ_NUM] : 0);
                    case MsgClasses.REFRESH:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_SEQ_NUM) > 0 ? _generalLong[MsgFields.Common.Long.SEQ_NUM] : 0);
                    case MsgClasses.GENERIC:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_SEQ_NUM) > 0 ? _generalLong[MsgFields.Common.Long.SEQ_NUM] : 0);
                    case MsgClasses.POST:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_SEQ_NUM) > 0 ? _generalLong[MsgFields.Common.Long.SEQ_NUM] : 0);
                    case MsgClasses.ACK:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_SEQ_NUM) > 0 ? _generalLong[MsgFields.Common.Long.SEQ_NUM] : 0);
                    default: // all other messages don't have sequence number
                        return 0;
                }
            }

            set
            {
                _generalLong[MsgFields.Common.Long.SEQ_NUM] = value;
            }
		}

        /// <summary>
        /// Checks the presence of the Acknowledgment indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckAck()
		{
			switch (MsgClass)
			{
				case MsgClasses.CLOSE:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & CloseMsgFlags.ACK) > 0 ? true : false);
				case MsgClasses.POST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.ACK) > 0 ? true : false);
				default:
					return false;
			}
		}

        /// <summary>
        /// Checks the presence of the Permission Expression presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasPermData()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_PERM_DATA) > 0 ? true : false);
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_PERM_DATA) > 0 ? true : false);
				case MsgClasses.STATUS:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_PERM_DATA) > 0 ? true : false);
				case MsgClasses.GENERIC:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_PERM_DATA) > 0 ? true : false);
				case MsgClasses.POST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_PERM_DATA) > 0 ? true : false);
				default:
					return false;
			}
		}

        /// <summary>
        /// Applies the Acknowledgment indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyAck()
		{
			switch (MsgClass)
			{
				case MsgClasses.CLOSE:
					_generalInt[MsgFields.Common.Int.FLAGS] |= CloseMsgFlags.ACK;
					break;
				case MsgClasses.REQUEST:
					break;
				case MsgClasses.POST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= PostMsgFlags.ACK;
					break;
				default:
					break;
			}
		}

        /// <summary>
        /// Applies the Permission Expression presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasPermData()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.HAS_PERM_DATA;
					break;
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.HAS_PERM_DATA;
					break;
				case MsgClasses.STATUS:
					_generalInt[MsgFields.Common.Int.FLAGS] |= StatusMsgFlags.HAS_PERM_DATA;
					break;
				case MsgClasses.GENERIC:
					_generalInt[MsgFields.Common.Int.FLAGS] |= GenericMsgFlags.HAS_PERM_DATA;
					break;
				case MsgClasses.POST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= PostMsgFlags.HAS_PERM_DATA;
					break;
				default:
					break;
			}
		}

        /// <summary>
		/// Gets or sets the part number of this generic message, typically used with
		/// multi-part generic messages. If sent on a single-part post message, use a
		/// partNum of 0. On multi-part post messages, use a partNum of 0 on the
		/// initial part and increment partNum in each subsequent part by 1.
		/// </summary>
		public int PartNum
		{
            get
            {
                switch (MsgClass)
                {
                    case MsgClasses.REFRESH:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_PART_NUM) > 0 ? _generalInt[MsgFields.Common.Int.PART_NUM] : 0);
                    case MsgClasses.GENERIC:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_PART_NUM) > 0 ? _generalInt[MsgFields.Common.Int.PART_NUM] : 0);
                    case MsgClasses.POST:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_PART_NUM) > 0 ? _generalInt[MsgFields.Common.Int.PART_NUM] : 0);
                    default: // all other messages don't have partNum
                        return 0;
                }
            }

            set
            {
                _generalInt[MsgFields.Common.Int.PART_NUM] = value;
            }
		}

        /// <summary>
		/// Gets or sets authorization information for this stream. When permData is
		/// specified, it indicates authorization information for only the content
		/// within this message, though this can be overridden for specific content
		/// within the message (e.g. MapEntry.permData).
		/// </summary>
		public Buffer PermData
		{
            get
            {
                if (_generalBuffer[MsgFields.Common.Buffer.PERM_DATA] == null)
                {
                    _generalBuffer[MsgFields.Common.Buffer.PERM_DATA] = new Buffer();
                }

                switch (MsgClass)
                {
                    case MsgClasses.UPDATE:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_PERM_DATA) > 0 ? _generalBuffer[MsgFields.Common.Buffer.PERM_DATA] : null);
                    case MsgClasses.REFRESH:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_PERM_DATA) > 0 ? _generalBuffer[MsgFields.Common.Buffer.PERM_DATA] : null);
                    case MsgClasses.STATUS:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_PERM_DATA) > 0 ? _generalBuffer[MsgFields.Common.Buffer.PERM_DATA] : null);
                    case MsgClasses.GENERIC:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_PERM_DATA) > 0 ? _generalBuffer[MsgFields.Common.Buffer.PERM_DATA] : null);
                    case MsgClasses.POST:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_PERM_DATA) > 0 ? _generalBuffer[MsgFields.Common.Buffer.PERM_DATA] : null);
                    default: // all other messages don't have permData
                        return null;
                }
            }

            set
            {
                if (_generalBuffer[MsgFields.Common.Buffer.PERM_DATA] == null)
                {
                    _generalBuffer[MsgFields.Common.Buffer.PERM_DATA] = new Buffer();
                }

            (_generalBuffer[MsgFields.Common.Buffer.PERM_DATA]).CopyReferences(value);
            }
		}

        /// <summary>
        /// Applies the Part Number presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasPartNum()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.HAS_PART_NUM;
					break;
				case MsgClasses.GENERIC:
					_generalInt[MsgFields.Common.Int.FLAGS] |= GenericMsgFlags.HAS_PART_NUM;
					break;
				case MsgClasses.POST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= PostMsgFlags.HAS_PART_NUM;
					break;
				default:
					break;
			}
		}

        /// <summary>
        /// Checks the presence of the Private Stream indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckPrivateStream()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.PRIVATE_STREAM) > 0 ? true : false);
				case MsgClasses.STATUS:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.PRIVATE_STREAM) > 0 ? true : false);
				case MsgClasses.REQUEST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.PRIVATE_STREAM) > 0 ? true : false);
				case MsgClasses.ACK:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.PRIVATE_STREAM) > 0 ? true : false);
				default:
					return false;
			}
		}

        /// <summary>
        /// Checks the presence of the Qualified Stream indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist.
        ///  </returns>
		public bool CheckQualifiedStream()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.QUALIFIED_STREAM) > 0 ? true : false);
				case MsgClasses.STATUS:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.QUALIFIED_STREAM) > 0 ? true : false);
				case MsgClasses.REQUEST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.QUALIFIED_STREAM) > 0 ? true : false);
				case MsgClasses.ACK:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.QUALIFIED_STREAM) > 0 ? true : false);
				default:
					return false;
			}
		}

        /// <summary>
		/// Gets or sets group identifier <seealso cref="Buffer"/> containing information about the item
		/// group to which this stream belongs. You can change the associated groupId
		/// via a subsequent <seealso cref="IStatusMsg"/> or <seealso cref="IRefreshMsg"/>. Group status
		/// notifications can change the state of an entire group of items.
		/// </summary>
		public Buffer GroupId
		{
            get
            {
                if (_generalBuffer[MsgFields.Common.Buffer.GROUP_ID] == null)
                {
                    _generalBuffer[MsgFields.Common.Buffer.GROUP_ID] = new Buffer();
                }

                switch (MsgClass)
                {
                    case MsgClasses.REFRESH: // refresh message always has group id
                        return _generalBuffer[MsgFields.Common.Buffer.GROUP_ID];
                    case MsgClasses.STATUS:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_GROUP_ID) > 0 ? _generalBuffer[MsgFields.Common.Buffer.GROUP_ID] : null);
                    default: // all other messages don't have group id
                        return null;
                }
            }

            set
            {
                if (_generalBuffer[MsgFields.Common.Buffer.GROUP_ID] == null)
                {
                    _generalBuffer[MsgFields.Common.Buffer.GROUP_ID] = new Buffer();
                }

                (_generalBuffer[MsgFields.Common.Buffer.GROUP_ID]).CopyReferences(value);
            }
		}

        /// <summary>
        /// Applies the Private Stream indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyPrivateStream()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.PRIVATE_STREAM;
					break;
				case MsgClasses.STATUS:
					_generalInt[MsgFields.Common.Int.FLAGS] |= StatusMsgFlags.PRIVATE_STREAM;
					break;
				case MsgClasses.REQUEST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.PRIVATE_STREAM;
					break;
				case MsgClasses.ACK:
					_generalInt[MsgFields.Common.Int.FLAGS] |= AckMsgFlags.PRIVATE_STREAM;
					break;
				default:
					break;
			}
		}

        /// <summary>
        /// Applies the Qualified Stream indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyQualifiedStream()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.QUALIFIED_STREAM;
					break;
				case MsgClasses.STATUS:
					_generalInt[MsgFields.Common.Int.FLAGS] |= StatusMsgFlags.QUALIFIED_STREAM;
					break;
				case MsgClasses.REQUEST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.QUALIFIED_STREAM;
					break;
				case MsgClasses.ACK:
					_generalInt[MsgFields.Common.Int.FLAGS] |= AckMsgFlags.QUALIFIED_STREAM;
					break;
				default:
					break;
			}
		}

        /// <summary>
        /// Checks the presence of the Part Number presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasPartNum()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_PART_NUM) > 0 ? true : false);
				case MsgClasses.GENERIC:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_PART_NUM) > 0 ? true : false);
				case MsgClasses.POST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_PART_NUM) > 0 ? true : false);
				default:
					return false;
			}
		}

        /// <summary>
		/// The ETA Post User Info Structure. This information can optionally be
		/// provided along with the posted content via the PostUserInfo on the
		/// <seealso cref="IRefreshMsg"/>, <seealso cref="IUpdateMsg"/>, and <seealso cref="IStatusMsg"/>.
		/// </summary>
		/// <returns> the postUserInfo </returns>
		public PostUserInfo PostUserInfo
		{
            get
            {
                switch (MsgClass)
                {
                    case MsgClasses.POST: // post message always has postUserInfo
                        return _generalPostUserInfo;
                    case MsgClasses.UPDATE:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_POST_USER_INFO) > 0 ? _generalPostUserInfo : null);
                    case MsgClasses.REFRESH:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_POST_USER_INFO) > 0 ? _generalPostUserInfo : null);
                    case MsgClasses.STATUS:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_POST_USER_INFO) > 0 ? _generalPostUserInfo : null);
                    default: // all other messages don't have postUserInfo
                        return null;
                }
            }
		}

        /// <summary>
        /// Checks the presence of the Quality of Service presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasQos()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_QOS) > 0 ? true : false);
				case MsgClasses.REQUEST:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_QOS) > 0 ? true : false);
				default:
					return false;
			}
		}

        /// <summary>
        /// Checks the presence of the Clear Cache indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckClearCache()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.CLEAR_CACHE) > 0 ? true : false);
				case MsgClasses.STATUS:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.CLEAR_CACHE) > 0 ? true : false);
				default:
					return false;
			}
		}

        /// <summary>
        /// Checks the presence of the Do Not Cache indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckDoNotCache()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.DO_NOT_CACHE) > 0 ? true : false);
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.DO_NOT_CACHE) > 0 ? true : false);
				default:
					return false;
			}
		}

        /// <summary>
        /// Checks the presence of the Post User Information presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasPostUserInfo()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_POST_USER_INFO) > 0 ? true : false);
				case MsgClasses.REFRESH:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_POST_USER_INFO) > 0 ? true : false);
				case MsgClasses.STATUS:
					return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_POST_USER_INFO) > 0 ? true : false);
				default:
					return false;
			}
		}

        /// <summary>
        /// Applies the Clear Cache indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        public void ApplyClearCache()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.CLEAR_CACHE;
					break;
				case MsgClasses.STATUS:
					_generalInt[MsgFields.Common.Int.FLAGS] |= StatusMsgFlags.CLEAR_CACHE;
					break;
				default:
					break;
			}
		}

        /// <summary>
        /// Applies the Do Not Cache indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyDoNotCache()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.DO_NOT_CACHE;
					break;
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.DO_NOT_CACHE;
					break;
				default:
					break;
			}
		}

        /// <summary>
        /// Applies the Post User Information presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        public void ApplyHasPostUserInfo()
		{
			switch (MsgClass)
			{
				case MsgClasses.UPDATE:
					_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.HAS_POST_USER_INFO;
					break;
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.HAS_POST_USER_INFO;
					break;
				case MsgClasses.STATUS:
					_generalInt[MsgFields.Common.Int.FLAGS] |= StatusMsgFlags.HAS_POST_USER_INFO;
					break;
				default:
					break;
			}
		}

        /// <summary>
		/// Gets or sets stream and data state information, which can change over time via
		/// subsequent refresh or status messages, or group status notifications.
		/// </summary>
		public State State
		{
            get
            {
                if (_generalState == null)
                {
                    _generalState = new State();
                }

                switch (MsgClass)
                {
                    case MsgClasses.REFRESH: // refresh message always has state
                        return _generalState;
                    case MsgClasses.STATUS:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_STATE) > 0 ? _generalState : null);
                    default: // all other messages don't have state
                        return null;
                }
            }
            
            set
            {
                if (_generalState == null)
                {
                    _generalState = new State();
                }

                value.Copy(_generalState);
            }
		}

        /// <summary>
		/// Gets the QoS of the stream. If a range was requested by the <seealso cref="IRequestMsg"/>
		/// , the qos should fall somewhere in this range, otherwise qos should
		/// exactly match what was requested..
		/// </summary>
		/// <returns> the qos </returns>
		public Qos Qos
		{
            get
            {
                if (_generalQos[MsgFields.Common.Qos.QOS] == null)
                {
                    _generalQos[MsgFields.Common.Qos.QOS] = new Qos();
                }

                switch (MsgClass)
                {
                    case MsgClasses.REFRESH:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.HAS_QOS) > 0 ? _generalQos[MsgFields.Common.Qos.QOS] : null);
                    case MsgClasses.REQUEST:
                        return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_QOS) > 0 ? _generalQos[MsgFields.Common.Qos.QOS] : null);
                    default: // all other messages don't have qos
                        return null;
                }
            }
		}

        ////////// begin AckMsg-specific methods //////////

        /// <summary>
        /// Checks the presence of the Text flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckHasText()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_TEXT) > 0 ? true : false);
		}

        /// <summary>
		/// Checks the presence of the NAK Code flag.<br />
		/// <br />
		/// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
		/// </summary>
		/// <seealso cref="IMsg.Flags"/>
		/// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasNakCode()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_NAK_CODE) > 0 ? true : false);
		}

        /// <summary>
        /// Applies the Text indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasText()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= AckMsgFlags.HAS_TEXT;
		}

        /// <summary>
        /// Applies the NAK Code indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasNakCode()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= AckMsgFlags.HAS_NAK_CODE;
		}

        /// <summary>
        /// Gets or sets ID used to associate this Ack with the message it is acknowledging.
        /// Valid range is 0-4294967296 (2^32).
        /// </summary>
		public long AckId
		{
            get
            {
                return _generalLong[MsgFields.Ack.Long.ACK_ID];
            }

            set
            {
                _generalLong[MsgFields.Ack.Long.ACK_ID] = value;
            }
		}

        /// <summary>
		/// Gets or sets this Ack indicates negative acknowledgment, if 0 or not
		/// present, Ack indicates positive acknowledgment.
		/// </summary>
		/// <seealso cref="NakCodes"/>
		public int NakCode
		{
            get
            {
                return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_NAK_CODE) > 0 ? _generalInt[MsgFields.Ack.Int.NAK_CODE] : 0);
            }

            set
            {
                _generalInt[MsgFields.Ack.Int.NAK_CODE] = value;
            }
		}

        /// <summary>
		/// Gets or sets text describes additional information about the acceptance or rejection
		/// of the message being acknowledged.
		/// </summary>
		public Buffer Text
		{
            get
            {
                if (_generalBuffer[MsgFields.Ack.Buffer.TEXT] == null)
                {
                    _generalBuffer[MsgFields.Ack.Buffer.TEXT] = new Buffer();
                }

                return ((_generalInt[MsgFields.Common.Int.FLAGS] & AckMsgFlags.HAS_TEXT) > 0 ? _generalBuffer[MsgFields.Ack.Buffer.TEXT] : null);
            }

            set
            {
                if (_generalBuffer[MsgFields.Ack.Buffer.TEXT] == null)
                {
                    _generalBuffer[MsgFields.Ack.Buffer.TEXT] = new Buffer();
                }

                (_generalBuffer[MsgFields.Ack.Buffer.TEXT]).CopyReferences(value);
            }
		}

        ////////// end AckMsg-specific methods //////////

        ////////// begin GenericMsg-specific methods //////////

        /// <summary>
        /// Checks the presence of the Message Complete indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckMessageComplete()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.MESSAGE_COMPLETE) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Secondary Sequence Number presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasSecondarySeqNum()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_SECONDARY_SEQ_NUM) > 0 ? true : false);
		}

        /// <summary>
        /// Applies the Message Complete indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyMessageComplete()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= GenericMsgFlags.MESSAGE_COMPLETE;
		}

        /// <summary>
        /// Applies the Secondary Sequence Number presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasSecondarySeqNum()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= GenericMsgFlags.HAS_SECONDARY_SEQ_NUM;
		}

        /// <summary>
		/// Gets or sets an additional user-defined sequence number. When using
		/// <seealso cref="IGenericMsg"/> on a stream in a bi-directional manner,
		/// secondarySeqNum is often used as an acknowledgment sequence number. For
		/// example, a consumer sends a generic message with seqNum populated to
		/// indicate the sequence of this message in the stream and secondarySeqNum
		/// set to the seqNum last received from the provider. This effectively
		/// acknowledges all messages received up to that point while also sending
		/// additional information. Sequence number use should be defined within the
		/// domain model specification or any documentation for products that use
		/// secondarySeqNum.
		/// </summary>
		public long SecondarySeqNum
		{
            get
            {
                return ((_generalInt[MsgFields.Common.Int.FLAGS] & GenericMsgFlags.HAS_SECONDARY_SEQ_NUM) > 0 ? _generalLong[MsgFields.Generic.Long.SECONDARY_SEQ_NUM] : 0);
            }

            set
            {
                _generalLong[MsgFields.Generic.Long.SECONDARY_SEQ_NUM] = value;
            }
		}

        ////////// end GenericMsg-specific methods //////////

        ////////// begin PostMsg-specific methods //////////
        
        /// <summary>
        /// Checks the presence of the Post Id presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckHasPostId()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_POST_ID) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Post Complete indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckPostComplete()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.POST_COMPLETE) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Post User Rights presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasPostUserRights()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_POST_USER_RIGHTS) > 0 ? true : false);
		}

        /// <summary>
        /// Applies the Post Id presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasPostId()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= PostMsgFlags.HAS_POST_ID;
		}

        /// <summary>
        /// Applies the Acknowledgment indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyPostComplete()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= PostMsgFlags.POST_COMPLETE;
		}

        /// <summary>
        /// Applies the Post User Rights presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        public void ApplyHasPostUserRights()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= PostMsgFlags.HAS_POST_USER_RIGHTS;
		}

        /// <summary>
		/// Gets or sets a consumer-assigned identifier that distinguishes different
		/// post messages. Each part in a multi-part post message should use the same
		/// postId value. Must be in the range of 0 - 4294967296 (2^32).
		/// </summary>
		public long PostId
		{
            get
            {
                return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_POST_ID) > 0 ? _generalLong[MsgFields.Post.Long.POST_ID] : 0);
            }

            set
            {
                _generalLong[MsgFields.Post.Long.POST_ID] = value;
            }
		}

        /// <summary>
        /// Gets or sets the rights or abilities of the user posting this content. This
        /// can indicate whether the user is permissioned to:
        /// <ul>
        /// <li>Create items in the cache of record. (0x01)</li>
        /// <li>Delete items from the cache of record. (0x02)</li>
        /// <li>Modify the permData on items already present in the cache of record. (0x03)</li>
        /// </ul>
        /// Must be in the range of 0 - 32767.
        /// </summary>
        /// <seealso cref="ThomsonReuters.Eta.Codec.PostUserRights"/>
		public int PostUserRights
		{
            get
            {
                return ((_generalInt[MsgFields.Common.Int.FLAGS] & PostMsgFlags.HAS_POST_USER_RIGHTS) > 0 ? _generalInt[MsgFields.Post.Int.POST_USER_RIGHTS] : 0);
            }

            set
            {
                _generalInt[MsgFields.Post.Int.POST_USER_RIGHTS] = value;
            }
		}

        ////////// end PostMsg-specific methods //////////

        ////////// begin RefreshMsg-specific methods //////////

        /// <summary>
        /// Checks the presence of the Solicited indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckSolicited()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.SOLICITED) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Refresh Complete indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckRefreshComplete()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RefreshMsgFlags.REFRESH_COMPLETE) > 0 ? true : false);
		}

        /// <summary>
        /// Applies the Solicited indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplySolicited()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.SOLICITED;
		}

        /// <summary>
        /// Applies the Refresh Complete indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyRefreshComplete()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.REFRESH_COMPLETE;
		}

        /// <summary>
        /// Applies the Quality of Service presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        public void ApplyHasQos()
		{
			switch (MsgClass)
			{
				case MsgClasses.REFRESH:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RefreshMsgFlags.HAS_QOS;
					break;
				case MsgClasses.REQUEST:
					_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.HAS_QOS;
					break;
				default:
				    break;
			}
		}

        ////////// end RefreshMsg-specific methods //////////

        ////////// begin RequestMsg-specific methods //////////

        /// <summary>
        /// Checks the presence of the Priority presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckHasPriority()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_PRIORITY) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Streaming indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckStreaming()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.STREAMING) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Message Key presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckMsgKeyInUpdates()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.MSG_KEY_IN_UPDATES) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Conflation Info presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckConfInfoInUpdates()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.CONF_INFO_IN_UPDATES) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the No Refresh indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckNoRefresh()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.NO_REFRESH) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Worst Quality of Service presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasWorstQos()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_WORST_QOS) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Pause indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckPause()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.PAUSE) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the View indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasView()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_VIEW) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Batch indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasBatch()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_BATCH) > 0 ? true : false);
		}

        /// <summary>
        /// Applies the Priority presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasPriority()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.HAS_PRIORITY;
		}

        /// <summary>
        /// Applies the Streaming indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyStreaming()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.STREAMING;
		}

        /// <summary>
        /// Applies the Message Key presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyMsgKeyInUpdates()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.MSG_KEY_IN_UPDATES;
		}

        /// <summary>
        /// Applies the Conflation Information in Updates indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyConfInfoInUpdates()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.CONF_INFO_IN_UPDATES;
		}

        /// <summary>
        /// Applies the No Refresh indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyNoRefresh()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.NO_REFRESH;
		}

        /// <summary>
        /// Applies the Worst Quality of Service presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasWorstQos()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.HAS_WORST_QOS;
		}

        /// <summary>
        /// Applies the Pause indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyPause()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.PAUSE;
		}

        /// <summary>
        /// Applies the View indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasView()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.HAS_VIEW;
		}

        /// <summary>
        /// Applies the Batch indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        public void ApplyHasBatch()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= RequestMsgFlags.HAS_BATCH;
		}

        /// <summary>
		/// Priority Structure (priority for the requested stream).
		/// </summary>
		/// <returns> the priority </returns>
		public Priority Priority
		{
            get
            {
                if (_generalPriority == null)
                {
                    _generalPriority = new Priority();
                }

                return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_PRIORITY) > 0 ? _generalPriority : null);
            }
		}

        /// <summary>
		/// The least acceptable QoS for the requested stream. When specified with a
		/// qos value, this is the worst in the range of allowable QoSs. When a QoS
		/// range is specified, any QoS within the range is acceptable for servicing the stream.
		/// </summary>
		/// <returns> the worstQos </returns>
		public Qos WorstQos
		{
            get
            {
                if (_generalQos[MsgFields.Request.Qos.WORST_QOS] == null)
                {
                    _generalQos[MsgFields.Request.Qos.WORST_QOS] = new Qos();
                }

                return ((_generalInt[MsgFields.Common.Int.FLAGS] & RequestMsgFlags.HAS_WORST_QOS) > 0 ? _generalQos[MsgFields.Request.Qos.WORST_QOS] : null);
            }
		}

        ////////// end RequestMsg-specific methods //////////

        ////////// begin StatusMsg-specific methods //////////

        /// <summary>
        /// Checks the presence of the Group Id presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckHasGroupId()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_GROUP_ID) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the State presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckHasState()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & StatusMsgFlags.HAS_STATE) > 0 ? true : false);
		}

        /// <summary>
        /// Applies the Group Id presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasGroupId()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= StatusMsgFlags.HAS_GROUP_ID;
		}

        /// <summary>
        /// Applies the State presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasState()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= StatusMsgFlags.HAS_STATE;
		}

        ////////// end StatusMsg-specific methods //////////

        ////////// begin UpdateMsg-specific methods //////////

        /// <summary>
        /// Checks the presence of the Conflation Information presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckHasConfInfo()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_CONF_INFO) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Do Not Conflate indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckDoNotConflate()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.DO_NOT_CONFLATE) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Do Not Ripple indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        public bool CheckDoNotRipple()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.DO_NOT_RIPPLE) > 0 ? true : false);
		}

        /// <summary>
        /// Checks the presence of the Discardable presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
		public bool CheckDiscardable()
		{
			return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.DISCARDABLE) > 0 ? true : false);
		}

        /// <summary>
        /// Applies the Conflation Info presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyHasConfInfo()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.HAS_CONF_INFO;
		}

        /// <summary>
        /// Applies the Do Not Conflate indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyDoNotConflate()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.DO_NOT_CONFLATE;
		}

        /// <summary>
        /// Applies the Do Not Ripple indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyDoNotRipple()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.DO_NOT_RIPPLE;
		}

        /// <summary>
        /// Applies the Discardable presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
		public void ApplyDiscardable()
		{
			_generalInt[MsgFields.Common.Int.FLAGS] |= UpdateMsgFlags.DISCARDABLE;
		}

        /// <summary>
		/// Gets or sets the type of data in the <seealso cref="IUpdateMsg"/>. Examples of possible
		/// update types include: Trade, Quote, or Closing Run.
		/// Must be in the range of 0 - 255.
		/// <ul>
		/// <li>
		/// Domain message model specifications define available update types</li>
		/// <li>
		/// For Thomson Reuters's provided domain models, see
		/// <seealso cref="Rdm.UpdateEventTypes"/></li>
		/// </ul>
		/// </summary>
		public int UpdateType
		{
            get
            {
                return _generalInt[MsgFields.Update.Int.UPDATE_TYPE];
            }

            set
            {
                _generalInt[MsgFields.Update.Int.UPDATE_TYPE] = value;
            }
		}

        /// <summary>
		/// When conflation is used, this value indicates the number of updates
		/// conflated or aggregated into this <seealso cref="IUpdateMsg"/>.
		/// Must be in the range of 0 - 32767.
		/// </summary>
		public int ConflationCount
		{
            get
            {
                return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_CONF_INFO) > 0 ? _generalInt[MsgFields.Update.Int.CONFLATION_COUNT] : 0);
            }

            set
            {
                _generalInt[MsgFields.Update.Int.CONFLATION_COUNT] = value;
            }
		}

        /// <summary>
		/// When conflation is used, this value indicates the period of time over
		/// which individual updates were conflated or aggregated into this
		/// <seealso cref="IUpdateMsg"/> (typically in milliseconds).
		/// Must be in the range of 0 - 65535.
		/// </summary>
		public int ConflationTime
		{
            get
            {
                return ((_generalInt[MsgFields.Common.Int.FLAGS] & UpdateMsgFlags.HAS_CONF_INFO) > 0 ? _generalInt[MsgFields.Update.Int.CONFLATION_TIME] : 0);
            }

            set
            {
                _generalInt[MsgFields.Update.Int.CONFLATION_TIME] = value;
            }
        }

		////////// end UpdateMsg-specific methods //////////

        /// <summary>
        /// Clears the current contents of the message and prepares it for re-use.
        /// Useful for object reuse during encoding.While decoding, { @link Msg}
        /// object can be reused without using {@link #clear()}.
        /// (Messages may be pooled in a single collection via their common
        /// {@link Msg}
        /// interface and re-used as a different { @link MsgClasses }).
        /// </summary>
		public void Clear()
		{
			MsgClass = 0;
			DomainType = 0;
			ContainerType = DataTypes.CONTAINER_TYPE_MIN;
			StreamId = 0;
			_msgKey.Clear();
			_extendedHeader.Clear();
			_encodedDataBody.Clear();
			_encodedMsgBuffer.Clear();

			_keyReserved = false;
			_extendedHeaderReserved = false;

			// clear the general-purpose fields:
            
			//Arrays.fill(_generalInt, 0);
            for (int i = 0; i < _generalInt.Length; ++i)
            {
                _generalInt[i] = 0;
            }
            //Arrays.fill(_generalLong, 0);
            for (int i = 0; i < _generalLong.Length; ++i)
            {
                _generalLong[i] = 0;
            }

            foreach (Buffer buf in _generalBuffer)
			{
				if (buf != null)
				{
					buf.Clear();
				}
			}

			_generalPostUserInfo.Clear();

			if (_generalState != null)
			{
				_generalState.Clear();
			}

			foreach (Qos qos in _generalQos)
			{
				if (qos != null)
				{
					qos.Clear();
				}
			}

			if (_generalPriority != null)
			{
				_generalPriority.Clear();
			}
		}

        internal void SetEncodedMsgBuffer(ByteBuffer encodedMsgBuffer, int position, int length)
        {
            (_encodedMsgBuffer).Data_internal(encodedMsgBuffer, position, length);
        }

        private CodecReturnCode CopyUpdateMsg(IUpdateMsg msg, int copyMsgFlags)
        {
            CodecReturnCode ret;
            msg.UpdateType = UpdateType;
            if (CheckHasExtendedHdr())
            {
                if ((copyMsgFlags & CopyMsgFlags.EXTENDED_HEADER) > 0)
                {
                    if ((ret = (_extendedHeader).CopyWithOrWithoutByteBuffer(msg.ExtendedHeader)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~UpdateMsgFlags.HAS_EXTENDED_HEADER;
                }
            }
            if (CheckHasPermData())
            {
                if ((copyMsgFlags & CopyMsgFlags.PERM_DATA) > 0)
                {
                    if ((ret = (PermData).CopyWithOrWithoutByteBuffer(msg.PermData)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~UpdateMsgFlags.HAS_PERM_DATA;
                }
            }
            if (CheckHasMsgKey())
            {
                if ((ret = ((MsgKey)MsgKey).Copy(msg.MsgKey, copyMsgFlags)) != CodecReturnCode.SUCCESS)
                {
                    return ret;
                }
            }
            if (EncodedDataBody.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.DATA_BODY) > 0)
                {
                    if ((ret = (EncodedDataBody).CopyWithOrWithoutByteBuffer(msg.EncodedDataBody)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (EncodedMsgBuffer.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.MSG_BUFFER) > 0)
                {
                    if ((ret = (EncodedMsgBuffer).CopyWithOrWithoutByteBuffer(msg.EncodedMsgBuffer)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (CheckHasPostUserInfo())
            {
                msg.PostUserInfo.UserAddr = PostUserInfo.UserAddr;
                msg.PostUserInfo.UserId = PostUserInfo.UserId;
                msg.ApplyHasPostUserInfo();
            }
            return CodecReturnCode.SUCCESS;
        }

        private CodecReturnCode CopyRefreshMsg(IRefreshMsg msg, int copyMsgFlags)
        {
            CodecReturnCode ret;
            if (CheckHasExtendedHdr())
            {
                if ((copyMsgFlags & CopyMsgFlags.EXTENDED_HEADER) > 0)
                {
                    if ((ret = (_extendedHeader).CopyWithOrWithoutByteBuffer(msg.ExtendedHeader)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~RefreshMsgFlags.HAS_EXTENDED_HEADER;
                }
            }
            if (CheckHasPermData())
            {
                if ((copyMsgFlags & CopyMsgFlags.PERM_DATA) > 0)
                {
                    if ((ret = (PermData).CopyWithOrWithoutByteBuffer(msg.PermData)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~RefreshMsgFlags.HAS_PERM_DATA;
                }
            }
            if (CheckHasMsgKey())
            {
                if ((ret = ((MsgKey)MsgKey).Copy(msg.MsgKey, copyMsgFlags)) != CodecReturnCode.SUCCESS)
                {
                    return ret;
                }
            }
            if (EncodedDataBody.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.DATA_BODY) > 0)
                {
                    if ((ret = (EncodedDataBody).CopyWithOrWithoutByteBuffer(msg.EncodedDataBody)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (EncodedMsgBuffer.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.MSG_BUFFER) > 0)
                {
                    if ((ret = (EncodedMsgBuffer).CopyWithOrWithoutByteBuffer(msg.EncodedMsgBuffer)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if ((copyMsgFlags & CopyMsgFlags.GROUP_ID) > 0)
            {
                if ((ret = (GroupId).CopyWithOrWithoutByteBuffer(msg.GroupId)) != CodecReturnCode.SUCCESS)
                {
                    return ret;
                }
            }
            msg.State.DataState(State.DataState());
            msg.State.StreamState(State.StreamState());
            msg.State.Code(State.Code());
            if ((copyMsgFlags & CopyMsgFlags.STATE_TEXT) > 0)
            {
                if ((ret = (State.Text()).CopyWithOrWithoutByteBuffer(msg.State.Text())) != CodecReturnCode.SUCCESS)
                {
                    return ret;
                }
            }
            if (CheckHasQos())
            {
                msg.Qos.Rate(Qos.Rate());
                msg.Qos.RateInfo(Qos.RateInfo());
                msg.Qos.Timeliness(Qos.Timeliness());
                msg.Qos.TimeInfo(Qos.TimeInfo());
                msg.Qos.IsDynamic = Qos.IsDynamic;
                msg.ApplyHasQos();
            }
            if (CheckHasPostUserInfo())
            {
                msg.PostUserInfo.UserAddr = PostUserInfo.UserAddr;
                msg.PostUserInfo.UserId = PostUserInfo.UserId;
                msg.ApplyHasPostUserInfo();
            }
            return CodecReturnCode.SUCCESS;
        }

        private CodecReturnCode CopyRequestMsg(IRequestMsg msg, int copyMsgFlags)
        {
            CodecReturnCode ret;
            if (CheckHasExtendedHdr())
            {
                if ((copyMsgFlags & CopyMsgFlags.EXTENDED_HEADER) > 0)
                {
                    if ((ret = (_extendedHeader).CopyWithOrWithoutByteBuffer(msg.ExtendedHeader)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~RequestMsgFlags.HAS_EXTENDED_HEADER;
                }
            }
            if ((ret = ((MsgKey)MsgKey).Copy(msg.MsgKey, copyMsgFlags)) != CodecReturnCode.SUCCESS)
            {
                return ret;
            }
            if (EncodedDataBody.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.DATA_BODY) > 0)
                {
                    if ((ret = (EncodedDataBody).CopyWithOrWithoutByteBuffer(msg.EncodedDataBody)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (EncodedMsgBuffer.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.MSG_BUFFER) > 0)
                {
                    if ((ret = (EncodedMsgBuffer).CopyWithOrWithoutByteBuffer(msg.EncodedMsgBuffer)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (CheckHasPriority())
            {
                msg.Priority.Count = Priority.Count;
                msg.Priority.PriorityClass = Priority.PriorityClass;
                msg.ApplyHasPriority();
            }
            if (CheckHasQos())
            {
                msg.Qos.Rate(Qos.Rate());
                msg.Qos.RateInfo(Qos.RateInfo());
                msg.Qos.Timeliness(Qos.Timeliness());
                msg.Qos.TimeInfo(Qos.TimeInfo());
                msg.Qos.IsDynamic = Qos.IsDynamic;
                msg.ApplyHasQos();
            }
            if (CheckHasWorstQos())
            {
                msg.WorstQos.Rate(WorstQos.Rate());
                msg.WorstQos.RateInfo(WorstQos.RateInfo());
                msg.WorstQos.Timeliness(WorstQos.Timeliness());
                msg.WorstQos.TimeInfo(WorstQos.TimeInfo());
                msg.WorstQos.IsDynamic = WorstQos.IsDynamic;
                msg.ApplyHasWorstQos();
            }
            return CodecReturnCode.SUCCESS;
        }

        private CodecReturnCode CopyPostMsg(IPostMsg msg, int copyMsgFlags)
        {
            CodecReturnCode ret;
            if (CheckHasExtendedHdr())
            {
                if ((copyMsgFlags & CopyMsgFlags.EXTENDED_HEADER) > 0)
                {
                    if ((ret = (_extendedHeader).CopyWithOrWithoutByteBuffer(msg.ExtendedHeader)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~PostMsgFlags.HAS_EXTENDED_HEADER;
                }
            }
            if (CheckHasPermData())
            {
                if ((copyMsgFlags & CopyMsgFlags.PERM_DATA) > 0)
                {
                    if ((ret = (PermData).CopyWithOrWithoutByteBuffer(msg.PermData)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~PostMsgFlags.HAS_PERM_DATA;
                }
            }
            if (CheckHasMsgKey())
            {
                if ((ret = ((MsgKey)MsgKey).Copy(msg.MsgKey, copyMsgFlags)) != CodecReturnCode.SUCCESS)
                {
                    return ret;
                }
            }
            if (EncodedDataBody.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.DATA_BODY) > 0)
                {
                    if ((ret = (EncodedDataBody).CopyWithOrWithoutByteBuffer(msg.EncodedDataBody)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (EncodedMsgBuffer.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.MSG_BUFFER) > 0)
                {
                    if ((ret = (EncodedMsgBuffer).CopyWithOrWithoutByteBuffer(msg.EncodedMsgBuffer)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            msg.PostUserInfo.UserAddr = PostUserInfo.UserAddr;
            msg.PostUserInfo.UserId = PostUserInfo.UserId;
            return CodecReturnCode.SUCCESS;
        }

        private CodecReturnCode CopyGenericMsg(IGenericMsg msg, int copyMsgFlags)
        {
            CodecReturnCode ret;
            if (CheckHasExtendedHdr())
            {
                if ((copyMsgFlags & CopyMsgFlags.EXTENDED_HEADER) > 0)
                {
                    if ((ret = (_extendedHeader).CopyWithOrWithoutByteBuffer(msg.ExtendedHeader)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~GenericMsgFlags.HAS_EXTENDED_HEADER;
                }
            }
            if (CheckHasPermData())
            {
                if ((copyMsgFlags & CopyMsgFlags.PERM_DATA) > 0)
                {
                    if ((ret = (PermData).CopyWithOrWithoutByteBuffer(msg.PermData)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~GenericMsgFlags.HAS_PERM_DATA;
                }
            }
            if (CheckHasMsgKey())
            {
                if ((ret = ((MsgKey)MsgKey).Copy(msg.MsgKey, copyMsgFlags)) != CodecReturnCode.SUCCESS)
                {
                    return ret;
                }
            }
            if (EncodedDataBody.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.DATA_BODY) > 0)
                {
                    if ((ret = (EncodedDataBody).CopyWithOrWithoutByteBuffer(msg.EncodedDataBody)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (EncodedMsgBuffer.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.MSG_BUFFER) > 0)
                {
                    if ((ret = (EncodedMsgBuffer).CopyWithOrWithoutByteBuffer(msg.EncodedMsgBuffer)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            return CodecReturnCode.SUCCESS;
        }

        private CodecReturnCode CopyCloseMsg(ICloseMsg msg, int copyMsgFlags)
        {
            CodecReturnCode ret;
            if (CheckHasExtendedHdr())
            {
                if ((copyMsgFlags & CopyMsgFlags.EXTENDED_HEADER) > 0)
                {
                    if ((ret = (_extendedHeader).CopyWithOrWithoutByteBuffer(msg.ExtendedHeader)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~CloseMsgFlags.HAS_EXTENDED_HEADER;
                }
            }
            if (EncodedDataBody.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.DATA_BODY) > 0)
                {
                    if ((ret = (EncodedDataBody).CopyWithOrWithoutByteBuffer(msg.EncodedDataBody)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (EncodedMsgBuffer.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.MSG_BUFFER) > 0)
                {
                    if ((ret = (EncodedMsgBuffer).CopyWithOrWithoutByteBuffer(msg.EncodedMsgBuffer)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            return CodecReturnCode.SUCCESS;
        }

        private CodecReturnCode CopyStatusMsg(IStatusMsg msg, int copyMsgFlags)
        {
            CodecReturnCode ret;
            if (CheckHasExtendedHdr())
            {
                if ((copyMsgFlags & CopyMsgFlags.EXTENDED_HEADER) > 0)
                {
                    if ((ret = (_extendedHeader).CopyWithOrWithoutByteBuffer(msg.ExtendedHeader)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~StatusMsgFlags.HAS_EXTENDED_HEADER;
                }
            }
            if (CheckHasPermData())
            {
                if ((copyMsgFlags & CopyMsgFlags.PERM_DATA) > 0)
                {
                    if ((ret = (PermData).CopyWithOrWithoutByteBuffer(msg.PermData)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~StatusMsgFlags.HAS_PERM_DATA;
                }
            }
            if (CheckHasMsgKey())
            {
                if ((ret = ((MsgKey)MsgKey).Copy(msg.MsgKey, copyMsgFlags)) != CodecReturnCode.SUCCESS)
                {
                    return ret;
                }
            }
            if (EncodedDataBody.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.DATA_BODY) > 0)
                {
                    if ((ret = (EncodedDataBody).CopyWithOrWithoutByteBuffer(msg.EncodedDataBody)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (EncodedMsgBuffer.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.MSG_BUFFER) > 0)
                {
                    if ((ret = (EncodedMsgBuffer).CopyWithOrWithoutByteBuffer(msg.EncodedMsgBuffer)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (CheckHasGroupId())
            {
                if ((copyMsgFlags & CopyMsgFlags.GROUP_ID) > 0)
                {
                    if ((ret = (GroupId).CopyWithOrWithoutByteBuffer(msg.GroupId)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~StatusMsgFlags.HAS_GROUP_ID;
                }
            }
            if (CheckHasState())
            {
                msg.State.DataState(State.DataState());
                msg.State.StreamState(State.StreamState());
                msg.State.Code(State.Code());
                if ((copyMsgFlags & CopyMsgFlags.STATE_TEXT) > 0)
                {
                    if ((ret = (State.Text()).CopyWithOrWithoutByteBuffer(msg.State.Text())) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (CheckHasPostUserInfo())
            {
                msg.PostUserInfo.UserAddr = PostUserInfo.UserAddr;
                msg.PostUserInfo.UserId = PostUserInfo.UserId;
                msg.ApplyHasPostUserInfo();
            }
            return CodecReturnCode.SUCCESS;
        }

        private CodecReturnCode CopyAckMsg(IAckMsg msg, int copyMsgFlags)
        {
            CodecReturnCode ret;
            if (CheckHasExtendedHdr())
            {
                if ((copyMsgFlags & CopyMsgFlags.EXTENDED_HEADER) > 0)
                {
                    if ((ret = (_extendedHeader).CopyWithOrWithoutByteBuffer(msg.ExtendedHeader)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~AckMsgFlags.HAS_EXTENDED_HEADER;
                }
            }
            if (CheckHasMsgKey())
            {
                if ((ret = ((MsgKey)MsgKey).Copy(msg.MsgKey, copyMsgFlags)) != CodecReturnCode.SUCCESS)
                {
                    return ret;
                }
            }
            if (EncodedDataBody.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.DATA_BODY) > 0)
                {
                    if ((ret = (EncodedDataBody).CopyWithOrWithoutByteBuffer(msg.EncodedDataBody)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (EncodedMsgBuffer.Length > 0)
            {
                if ((copyMsgFlags & CopyMsgFlags.MSG_BUFFER) > 0)
                {
                    if ((ret = (EncodedMsgBuffer).CopyWithOrWithoutByteBuffer(msg.EncodedMsgBuffer)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
            }
            if (CheckHasText())
            {
                if ((copyMsgFlags & CopyMsgFlags.NAK_TEXT) > 0)
                {
                    if ((ret = (Text).CopyWithOrWithoutByteBuffer(msg.Text)) != CodecReturnCode.SUCCESS)
                    {
                        return ret;
                    }
                }
                else
                {
                    msg.Flags = msg.Flags & ~AckMsgFlags.HAS_TEXT;
                }
            }
            return CodecReturnCode.SUCCESS;
        }

    }
}