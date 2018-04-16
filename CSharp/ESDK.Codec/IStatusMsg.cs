﻿/*|-----------------------------------------------------------------------------
 *|            This source code is provided under the Apache 2.0 license      --
 *|  and is provided AS IS with no warranty or guarantee of fit for purpose.  --
 *|                See the project's LICENSE.md for details.                  --
 *|           Copyright Thomson Reuters 2018. All rights reserved.            --
 *|-----------------------------------------------------------------------------
 */

namespace ThomsonReuters.Eta.Codec
{

	/// <summary>
	/// ETA Status Message is used to indicate changes to the stream or data
	/// properties. This message can convey changes in streamState or dataState,
	/// changes in a stream's permissioning information, and changes to the item
	/// group that the stream is part of. A Provider application uses the
	/// <see cref="IStatusMsg"/> to close streams to a Consumer, both in conjunction with an
	/// initial request or at some point after the stream has been established. A
	/// <see cref="IStatusMsg"/> can also be used to indicate successful establishment of a
	/// stream, even though the message may not contain any data - this can be useful
	/// when establishing a stream solely to exchange bi-directional <seealso cref="IGenericMsg"/>.
	/// </summary>
	/// <seealso cref="IMsg"/>
	/// <seealso cref="StatusMsgFlags"/>
	public interface IStatusMsg : IMsg
	{
		/// <summary>
		/// Checks the presence of the Extended Header presence flag.<br />
		/// <br />
		/// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
		/// </summary>
		/// <seealso cref="IMsg.Flags"/>
		/// <returns> true - if exists; false if does not exist. </returns>
		bool CheckHasExtendedHdr();

        /// <summary>
        /// Checks the presence of the Permission Expression presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        bool CheckHasPermData();

        /// <summary>
        /// Checks the presence of the Message Key presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        bool CheckHasMsgKey();

        /// <summary>
        /// Checks the presence of the Group Id presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        bool CheckHasGroupId();

        /// <summary>
        /// Checks the presence of the State presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        bool CheckHasState();

        /// <summary>
        /// Checks the presence of the Clear Cache indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        bool CheckClearCache();

        /// <summary>
        /// Checks the presence of the Private Stream indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist.
        ///  </returns>
        bool CheckPrivateStream();

        /// <summary>
        /// Checks the presence of the Post User Information presence flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        bool CheckHasPostUserInfo();

        /// <summary>
        /// Checks the presence of the Qualified Stream indication flag.<br />
        /// <br />
        /// Flags may also be bulk-get via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        /// <returns> true - if exists; false if does not exist. </returns>
        bool CheckQualifiedStream();

        /// <summary>
        /// Applies the Extended Header presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        void ApplyHasExtendedHdr();

        /// <summary>
        /// Applies the Permission Expression presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        void ApplyHasPermData();

        /// <summary>
        /// Applies the Message Key presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        void ApplyHasMsgKey();

        /// <summary>
        /// Applies the Group Id presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        void ApplyHasGroupId();

        /// <summary>
        /// Applies the State presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        void ApplyHasState();

        /// <summary>
        /// Applies the Clear Cache indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        void ApplyClearCache();

        /// <summary>
        /// ** Applies the Private Stream indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        void ApplyPrivateStream();

        /// <summary>
        /// Applies the Post User Information presence flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        void ApplyHasPostUserInfo();

        /// <summary>
        /// Applies the Qualified Stream indication flag.<br />
        /// <br />
        /// Flags may also be bulk-set via <see cref="IMsg.Flags"/>.
        /// </summary>
        /// <seealso cref="IMsg.Flags"/>
        void ApplyQualifiedStream();

		/// <summary>
		/// Gets or sets stream and data state information, which can change over time via
		/// subsequent refresh or status messages, or group status notifications.
		/// </summary>
		State State { get; set; }

		/// <summary>
		/// Gets or sets <seealso cref="Buffer"/> containing information about the item group to which
		/// this stream belongs. A subsequent <seealso cref="IStatusMsg"/> or <seealso cref="IRefreshMsg"/>
		/// can change the item group's associated groupId, while group status
		/// notifications can change the state of an entire group of items.
		/// </summary>
		Buffer GroupId { get; set; }

        /// <summary>
        /// Gets or sets authorization information for this stream. When specified,
        /// permData indicates authorization information for only the content within
        /// this message, though this can be overridden for specific content within
        /// the message (e.g. MapEntry.permData).
        /// </summary>
        Buffer PermData { get; set; }

		/// <summary>
		/// Identifies the user who posted this information.
		/// </summary>
		/// <returns> the postUserInfo </returns>
		PostUserInfo PostUserInfo { get; }
	}
}