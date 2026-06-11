package com.vkenterprises.crmrs.data.local

import android.content.Context
import androidx.room.Room
import com.vkenterprises.crmrs.data.api.SessionTokens
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class TenantDb @Inject constructor(
    @ApplicationContext private val context: Context
) {
    @Volatile private var instance: VKDatabase? = null
    @Volatile private var openedFor: String? = null

    init {
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
