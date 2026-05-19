package com.vkenterprises.vras.utils

import android.content.Context
import androidx.datastore.preferences.core.*
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.dataStore by preferencesDataStore(name = "vk_prefs")

class PreferencesManager(private val context: Context) {

    companion object {
        val KEY_USER_ID        = longPreferencesKey("user_id")
        val KEY_NAME           = stringPreferencesKey("name")
        val KEY_MOBILE         = stringPreferencesKey("mobile")
        val KEY_IS_ADMIN       = booleanPreferencesKey("is_admin")
        val KEY_SUB_END        = stringPreferencesKey("sub_end")
        val KEY_LOGGED_IN      = booleanPreferencesKey("logged_in")
        val KEY_PFP            = stringPreferencesKey("pfp")
        val KEY_BLOCKED_REASON = stringPreferencesKey("blocked_reason")
        // Multi-tenant: the agency this device belongs to + its signed token.
        val KEY_TENANT_TOKEN   = stringPreferencesKey("tenant_token")
        val KEY_AGENCY_SLUG    = stringPreferencesKey("agency_slug")
        val KEY_AGENCY_NAME    = stringPreferencesKey("agency_name")
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

    val userMobile: Flow<String> = context.dataStore.data
        .map { it[KEY_MOBILE] ?: "" }

    val pfpUrl: Flow<String?> = context.dataStore.data
        .map { it[KEY_PFP]?.ifEmpty { null } }

    val blockedReason: Flow<String?> = context.dataStore.data
        .map { it[KEY_BLOCKED_REASON]?.ifBlank { null } }

    val tenantToken: Flow<String?> = context.dataStore.data
        .map { it[KEY_TENANT_TOKEN]?.ifBlank { null } }

    val agencySlug: Flow<String?> = context.dataStore.data
        .map { it[KEY_AGENCY_SLUG]?.ifBlank { null } }

    val agencyName: Flow<String?> = context.dataStore.data
        .map { it[KEY_AGENCY_NAME]?.ifBlank { null } }

    suspend fun saveSession(
        userId: Long, name: String, mobile: String,
        isAdmin: Boolean, subEnd: String?, pfp: String? = null,
        tenantToken: String? = null
    ) {
        context.dataStore.edit { prefs ->
            prefs[KEY_USER_ID]   = userId
            prefs[KEY_NAME]      = name
            prefs[KEY_MOBILE]    = mobile
            prefs[KEY_IS_ADMIN]  = isAdmin
            prefs[KEY_SUB_END]   = subEnd ?: ""
            prefs[KEY_LOGGED_IN] = true
            if (pfp != null) prefs[KEY_PFP] = pfp
            if (tenantToken != null) prefs[KEY_TENANT_TOKEN] = tenantToken
        }
    }

    // Remembers the agency the user picked, so the login screen can pre-fill it.
    suspend fun saveAgency(slug: String, name: String) {
        context.dataStore.edit { prefs ->
            prefs[KEY_AGENCY_SLUG] = slug
            prefs[KEY_AGENCY_NAME] = name
        }
    }

    suspend fun updateAdminStatus(isAdmin: Boolean) {
        context.dataStore.edit { prefs ->
            prefs[KEY_IS_ADMIN] = isAdmin
        }
    }

    suspend fun savePfp(pfp: String?) {
        context.dataStore.edit { prefs ->
            prefs[KEY_PFP] = pfp ?: ""
        }
    }

    suspend fun setBlockedReason(reason: String) {
        context.dataStore.edit { prefs -> prefs[KEY_BLOCKED_REASON] = reason }
    }

    suspend fun clearBlockedReason() {
        context.dataStore.edit { prefs -> prefs.remove(KEY_BLOCKED_REASON) }
    }

    suspend fun clearSession() {
        // Drop the session + tenant token, but keep the chosen agency so the
        // user doesn't have to pick it again on the next login.
        context.dataStore.edit { prefs ->
            val slug = prefs[KEY_AGENCY_SLUG]
            val name = prefs[KEY_AGENCY_NAME]
            prefs.clear()
            if (slug != null) prefs[KEY_AGENCY_SLUG] = slug
            if (name != null) prefs[KEY_AGENCY_NAME] = name
        }
    }
}
