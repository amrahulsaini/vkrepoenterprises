package com.vkenterprises.vras.utils

import android.content.Context
import android.provider.Settings

object DeviceIdUtil {
    fun get(context: Context): String =
        Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID)
            ?: "unknown_device"
}
