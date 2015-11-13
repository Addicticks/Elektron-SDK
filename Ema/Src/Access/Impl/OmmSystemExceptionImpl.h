/*|-----------------------------------------------------------------------------
 *|            This source code is provided under the Apache 2.0 license      --
 *|  and is provided AS IS with no warranty or guarantee of fit for purpose.  --
 *|                See the project's LICENSE.md for details.                  --
 *|           Copyright Thomson Reuters 2015. All rights reserved.            --
 *|-----------------------------------------------------------------------------
 */

#ifndef __thomsonreuters_ema_access_OmmSystemExceptionImpl_h
#define __thomsonreuters_ema_access_OmmSystemExceptionImpl_h

#include "OmmSystemException.h"

namespace thomsonreuters {

namespace ema {

namespace access {

class EMA_ACCESS_API OmmSystemExceptionImpl : public OmmSystemException
{
public :

	static void throwException( const char* , Int64 , void* );

	static void throwException( const EmaString& , Int64 , void* );

	OmmSystemExceptionImpl();

	virtual ~OmmSystemExceptionImpl();

private :

	OmmSystemExceptionImpl( const OmmSystemExceptionImpl& );
	OmmSystemExceptionImpl& operator=( const OmmSystemExceptionImpl& );
};

}

}

}

#endif // __thomsonreuters_ema_access_OmmSystemExceptionImpl_h