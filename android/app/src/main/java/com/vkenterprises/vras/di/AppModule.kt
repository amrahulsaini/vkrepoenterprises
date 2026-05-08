package com.vkenterprises.vras.di

import android.content.Context
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

    @Provides
    @Singleton
    fun providePreferencesManager(@ApplicationContext ctx: Context): PreferencesManager =
        PreferencesManager(ctx)
}
