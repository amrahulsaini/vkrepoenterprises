package com.vkenterprises.vras.data.local

import android.content.Context
import androidx.room.Room
import com.vkenterprises.vras.data.api.SessionTokens
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Per-agency Room database. Each agency the user signs in to gets its own
 *   vk_cache_<slug>.db
 * file — switching agencies opens a different file entirely, so one agency's
 * offline records can NEVER be queried while signed in to another.
 *
 * All DAO consumers inject TenantDb and call vehicleCacheDao() / etc. on each
 * use so the lookup always reflects the current value of SessionTokens.agencySlug.
 */
@Singleton
class TenantDb @Inject constructor(
    @ApplicationContext private val context: Context
) {
    @Volatile private var instance: VKDatabase? = null
    @Volatile private var openedFor: String? = null

    init {
        // The pre-multi-tenant builds wrote everything to a single shared
        // vk_cache.db. It's never read again — delete it on first run so
        // there is no chance of a stale row leaking under the new scheme.
        runCatching { context.deleteDatabase("vk_cache.db") }
    }

    @Synchronized
    fun get(): VKDatabase {
        val slug = SessionTokens.agencySlug?.takeIf { it.isNotBlank() } ?: "none"
        if (openedFor != slug || instance == null) {
            instance?.close()
            instance = Room.databaseBuilder(
                context,
                VKDatabase::class.java,
                "vk_cache_${slug}.db"
            )
                .fallbackToDestructiveMigration()
                .build()
            openedFor = slug
        }
        return instance!!
    }

    fun vehicleCacheDao(): VehicleCacheDao       = get().vehicleCacheDao()
    fun branchSyncStateDao(): BranchSyncStateDao = get().branchSyncStateDao()
}
