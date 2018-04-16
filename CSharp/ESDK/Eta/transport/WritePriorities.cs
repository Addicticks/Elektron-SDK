﻿/*|-----------------------------------------------------------------------------
 *|            This source code is provided under the Apache 2.0 license      --
 *|  and is provided AS IS with no warranty or guarantee of fit for purpose.  --
 *|                See the project's LICENSE.md for details.                  --
 *|           Copyright Thomson Reuters 2018. All rights reserved.            --
 *|-----------------------------------------------------------------------------
 */

using ThomsonReuters.Eta.Transports.Interfaces;

namespace ThomsonReuters.Eta.Transports
{
    /// <summary>
    /// ETA Write Priorities passed into the <see cref="IChannel.Write(ITransportBuffer, WriteArgs, out Error)"/> method call.
    /// </summary>
    public enum WritePriorities
    {
        /** Assigns message to the high priority flush, if not directly written to the socket. */
        HIGH = 0,

        /** Assigns message to the medium priority flush, if not directly written to the socket. */
        MEDIUM = 1,

        /** Assigns message to the low priority flush, if not directly written to the socket. */
        LOW = 2
    }
}
