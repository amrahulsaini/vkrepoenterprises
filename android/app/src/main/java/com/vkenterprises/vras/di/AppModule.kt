package com.vkenterprises.vras.di

import android.content.Context
import androidx.room.Room
import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.data.local.*
import com.vkenterprises.vras.utils.PreferencesManager
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object AppModule {

    @Provides @Singleton
    fun providePreferencesManager(@ApplicationContext ctx: Context): PreferencesManager =
        PreferencesManager(ctx)

    @Provides @Singleton
    fun provideVKDatabase(@ApplicationContext ctx: Context): VKDatabase =
        Room.databaseBuilder(ctx, VKDatabase::class.java, "vk_cache.db").build()

    @Provides
    fun provideVehicleCacheDao(db: VKDatabase): VehicleCacheDao = db.vehicleCacheDao()

    @Provides
    fun provideBranchSyncStateDao(db: VKDatabase): BranchSyncStateDao = db.branchSyncStateDao()

    @Provides @Singleton
    fun provideApiService() = ApiClient.api
}
