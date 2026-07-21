package com.vkenterprises.crmrs.utils

import android.content.Context
import android.provider.Settings
import java.util.UUID

// Settings.Secure.ANDROID_ID is scoped per {app signing certificate, user, device}
// since Android 8 — Google's own docs warn its value changes if the APK signing
// key changes. That includes Google Play App Signing re-signing the app for
// distribution (the default/required option for App Bundles): every existing
// user's ANDROID_ID would change on their next update, mass-triggering the
// device-lock's "different device" rejection for accounts that never moved.
//
// Fix: persist a device id in SharedPreferences instead of recomputing it every
// call. The FIRST time this runs on any given install, seed it from the
// current ANDROID_ID (not a fresh random value) so every account already
// registered under the old scheme keeps matching after this update — only
// installs that never had a stored id get a fresh random UUID. From then on
// every future call reads the persisted value, so it never again changes for
// this install regardless of any later signing-key change.
object DeviceIdUtil {
    private const val PREFS_NAME = "device_id_prefs"
    private const val KEY_DEVICE_ID = "device_id"

    fun get(context: Context): String {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        prefs.getString(KEY_DEVICE_ID, null)?.let { return it }
        val seeded = Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID)
            ?.takeIf { it.isNotBlank() }
            ?: UUID.randomUUID().toString()
        prefs.edit().putString(KEY_DEVICE_ID, seeded).apply()
        return seeded
    }
}
