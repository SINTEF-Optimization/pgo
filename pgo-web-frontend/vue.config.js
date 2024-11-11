const path = require("path")
const webpack = require('webpack')

let outputDir = process.env.PGO_FRONTEND_BUILD_OUTPUT_DIR || "dist"
if (!path.isAbsolute(outputDir)) {
  outputDir = path.resolve(__dirname, outputDir)
}

module.exports = {
  outputDir: outputDir,
  assetsDir: 'pgo-web-frontend',
  devServer: {
    proxy: {
      '^/': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        ws: true,
        logLevel: 'debug',
      },
    },
  },
  css: {
    loaderOptions: {
      sass: {
        additionalData: `@import "@/style/main.scss";`,
      },
    },
  },
  configureWebpack: {
    node: {
      // Add setImmediate polyfills for compatibility with Sigma WebGlRenderer
      // This is disabled by default when the app is scaffolded using Vue CLI
      setImmediate: true,
    },
    devtool: 'source-map',
    plugins: [
      // Provide Sigma (v1) to all modules as if it's being imported. This is necessary
      // because of the way Sigma registers plug-ins (assuming there's a "sigma" in the global scope)
      new webpack.ProvidePlugin({
        sigma: 'sigma',
      }),
    ],
  },
}
