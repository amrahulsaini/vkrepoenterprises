package com.vkenterprises.vras.di

import android.content.Context
import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.utils.PreferencesManager
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

// Note: the local Room database is no longer provided here. Each consumer
// injects TenantDb (a @Singleton itself) and fetches DAOs on demand — see
// TenantDb for the per-agency / per-file isolation guarantee.
@Module
@InstallIn(SingletonComponent::class)
object AppModule {

    @Provides @Singleton
    fun providePreferencesManager(@ApplicationContext ctx: Context): PreferencesManager =
        PreferencesManager(ctx)

    @Provides @Singleton
    fun provideApiService() = ApiClient.api
}
