package com.vkenterprises.vras.utils

import android.content.Context
import androidx.datastore.preferences.core.*
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.dataStore by preferencesDataStore(name = "vk_prefs")

class PreferencesManager(private val context: Context) {

    companion object {
        val KEY_USER_ID     = longPreferencesKey("user_id")
        val KEY_NAME        = stringPreferencesKey("name")
        val KEY_MOBILE      = stringPreferencesKey("mobile")
        val KEY_IS_ADMIN    = booleanPreferencesKey("is_admin")
        val KEY_SUB_END     = stringPreferencesKey("sub_end")
        val KEY_LOGGED_IN   = booleanPreferencesKey("logged_in")
    }

    val isLoggedIn: Flow<Boolean> = context.dataStore.data
        .map { it[KEY_LOGGED_IN] ?: false }

    val userId: Flow<Long> = context.dataStore.data
        .map { it[KEY_USER_ID] ?: -1L }

    val userName: Flow<String> = context.dataStore.data
        .map { it[KEY_NAME] ?: "" }

    val isAdmin: Flow<Boolean> = context.dataStore.data
        .map { it[KEY_IS_ADMIN] ?: false }

    val subscriptionEnd: Flow<String?> = context.dataStore.data
        .map { it[KEY_SUB_END] }

    suspend fun saveSession(
        userId: Long, name: String, mobile: String,
        isAdmin: Boolean, subEnd: String?
    ) {
        context.dataStore.edit { prefs ->
            prefs[KEY_USER_ID]   = userId
            prefs[KEY_NAME]      = name
            prefs[KEY_MOBILE]    = mobile
            prefs[KEY_IS_ADMIN]  = isAdmin
            prefs[KEY_SUB_END]   = subEnd ?: ""
            prefs[KEY_LOGGED_IN] = true
        }
    }

    suspend fun clearSession() {
        context.dataStore.edit { it.clear() }
    }
}
