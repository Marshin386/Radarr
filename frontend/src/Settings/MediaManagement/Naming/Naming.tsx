import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputButton from 'Components/Form/FormInputButton';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import useModalOpenState from 'Helpers/Hooks/useModalOpenState';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import {
  fetchNamingExamples,
  fetchNamingSettings,
  setNamingSettingsValue,
} from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import { InputChanged } from 'typings/inputs';
import NamingConfig from 'typings/Settings/NamingConfig';
import translate from 'Utilities/String/translate';
import NamingModal from './NamingModal';
import styles from './Naming.css';

const SECTION = 'naming';

function createNamingSelector() {
  return createSelector(
    (state: AppState) => state.settings.namingExamples,
    createSettingsSectionSelector(SECTION),
    (namingExamples, sectionSettings) => {
      return {
        examples: namingExamples.item,
        examplesPopulated: namingExamples.isPopulated,
        ...sectionSettings,
      };
    }
  );
}

interface NamingModalOptions {
  name: keyof Pick<
    NamingConfig,
    'standardMovieFormat' | 'movieFolderFormat' | 'customMovieFolderFormat'
  >;
  movie?: boolean;
  additional?: boolean;
}

function Naming() {
  const {
    isFetching,
    error,
    settings,
    hasSettings,
    examples,
    examplesPopulated,
  } = useSelector(createNamingSelector());

  const dispatch = useDispatch();

  const [isNamingModalOpen, setNamingModalOpen, setNamingModalClosed] =
    useModalOpenState(false);
  const [namingModalOptions, setNamingModalOptions] =
    useState<NamingModalOptions | null>(null);
  const namingExampleTimeout = useRef<ReturnType<typeof setTimeout>>();

  useEffect(() => {
    dispatch(fetchNamingSettings());
    dispatch(fetchNamingExamples());

    return () => {
      dispatch(clearPendingChanges({ section: 'settings.naming' }));
    };
  }, [dispatch]);

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error 'setNamingSettingsValue' isn't typed yet
      dispatch(setNamingSettingsValue(change));

      if (namingExampleTimeout.current) {
        clearTimeout(namingExampleTimeout.current);
      }

      namingExampleTimeout.current = setTimeout(() => {
        dispatch(fetchNamingExamples());
      }, 1000);
    },
    [dispatch]
  );

  const onStandardNamingModalOpenClick = useCallback(() => {
    setNamingModalOpen();

    setNamingModalOptions({
      name: 'standardMovieFormat',
      movie: true,
      additional: true,
    });
  }, [setNamingModalOpen, setNamingModalOptions]);

  const onMovieFolderNamingModalOpenClick = useCallback(() => {
    setNamingModalOpen();

    setNamingModalOptions({
      name: 'movieFolderFormat',
    });
  }, [setNamingModalOpen, setNamingModalOptions]);

  const onCustomMovieFolderNamingModalOpenClick = useCallback(() => {
    setNamingModalOpen();

    setNamingModalOptions({
      name: 'customMovieFolderFormat',
    });
  }, [setNamingModalOpen, setNamingModalOptions]);

  const renameMovies = hasSettings && settings.renameMovies.value;
  const replaceIllegalCharacters =
    hasSettings && settings.replaceIllegalCharacters.value;

  const libraryStructure = settings.folderLayout?.value || 'movieFolder';
  const libraryStructureOptions = [
    {
      key: 'movieFolder',
      value: translate('MovieFolderLayoutMovieFolder'),
    },
    { key: 'flat', value: translate('MovieFolderLayoutFlat') },
    { key: 'custom', value: translate('MovieFolderLayoutCustom') },
  ];

  const colonReplacementOptions = [
    { key: 'delete', value: translate('Delete') },
    { key: 'dash', value: translate('ReplaceWithDash') },
    { key: 'spaceDash', value: translate('ReplaceWithSpaceDash') },
    { key: 'spaceDashSpace', value: translate('ReplaceWithSpaceDashSpace') },
    {
      key: 'smart',
      value: translate('SmartReplace'),
      hint: translate('SmartReplaceHint'),
    },
  ];

  const standardMovieFormatHelpTexts = [];
  const standardMovieFormatErrors = [];
  const movieFolderFormatHelpTexts = [];
  const movieFolderFormatErrors = [];
  const customMovieFolderFormatHelpTexts = [];
  const customMovieFolderFormatErrors = [];

  if (examplesPopulated) {
    if (examples.movieExample) {
      standardMovieFormatHelpTexts.push(
        `${translate('Movie')}: ${examples.movieExample}`
      );
    } else {
      standardMovieFormatErrors.push({
        message: translate('MovieInvalidFormat'),
      });
    }

    if (examples.movieFolderExample) {
      movieFolderFormatHelpTexts.push(
        `${translate('Example')}: ${examples.movieFolderExample}`
      );
    } else {
      movieFolderFormatErrors.push({ message: translate('InvalidFormat') });
    }

    if (examples.customMovieFolderExample) {
      customMovieFolderFormatHelpTexts.push(
        `${translate('Example')}: ${examples.customMovieFolderExample}`
      );
    } else if (settings.customMovieFolderFormat?.value) {
      customMovieFolderFormatErrors.push({ message: translate('InvalidFormat') });
    }
  }

  return (
    <FieldSet legend={translate('MovieNaming')}>
      {isFetching ? <LoadingIndicator /> : null}

      {!isFetching && error ? (
        <Alert kind={kinds.DANGER}>
          {translate('NamingSettingsLoadError')}
        </Alert>
      ) : null}

      {hasSettings && !isFetching && !error ? (
        <Form>
          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('RenameMovies')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="renameMovies"
              helpText={translate('RenameMoviesHelpText')}
              onChange={handleInputChange}
              {...settings.renameMovies}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('ReplaceIllegalCharacters')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="replaceIllegalCharacters"
              helpText={translate('ReplaceIllegalCharactersHelpText')}
              onChange={handleInputChange}
              {...settings.replaceIllegalCharacters}
            />
          </FormGroup>

          {replaceIllegalCharacters ? (
            <FormGroup size={sizes.MEDIUM}>
              <FormLabel>{translate('ColonReplacement')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="colonReplacementFormat"
                values={colonReplacementOptions}
                helpText={translate('ColonReplacementFormatHelpText')}
                onChange={handleInputChange}
                {...settings.colonReplacementFormat}
              />
            </FormGroup>
          ) : null}

          {renameMovies ? (
            <FormGroup size={sizes.LARGE}>
              <FormLabel>{translate('StandardMovieFormat')}</FormLabel>

              <FormInputGroup
                inputClassName={styles.namingInput}
                type={inputTypes.TEXT}
                name="standardMovieFormat"
                buttons={
                  <FormInputButton onPress={onStandardNamingModalOpenClick}>
                    ?
                  </FormInputButton>
                }
                onChange={handleInputChange}
                {...settings.standardMovieFormat}
                helpTexts={standardMovieFormatHelpTexts}
                errors={[
                  ...standardMovieFormatErrors,
                  ...settings.standardMovieFormat.errors,
                ]}
              />
            </FormGroup>
          ) : null}

          <FormGroup size={sizes.MEDIUM} data-testid="library-structure">
            <FormLabel>{translate('LibraryStructure')}</FormLabel>

            <FormInputGroup
              type={inputTypes.SELECT}
              name="folderLayout"
              values={libraryStructureOptions}
              helpText={translate('LibraryStructureHelpText')}
              onChange={handleInputChange}
              {...settings.folderLayout}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('MovieFolderFormat')}</FormLabel>

            <FormInputGroup
              inputClassName={styles.namingInput}
              type={inputTypes.TEXT}
              name="movieFolderFormat"
              buttons={
                <FormInputButton
                  onPress={onMovieFolderNamingModalOpenClick}
                  isDisabled={libraryStructure !== 'movieFolder'}
                >
                  ?
                </FormInputButton>
              }
              onChange={handleInputChange}
              {...settings.movieFolderFormat}
              isDisabled={libraryStructure !== 'movieFolder'}
              helpTexts={[
                translate('MovieFolderFormatHelpText'),
                ...movieFolderFormatHelpTexts,
              ]}
              helpTextWarning={translate(
                'MovieFolderFormatHelpTextDeprecatedWarning'
              )}
              errors={[
                ...movieFolderFormatErrors,
                ...settings.movieFolderFormat.errors,
              ]}
            />
          </FormGroup>

          {settings.customMovieFolderFormat ? (
            <FormGroup>
              <FormLabel>{translate('CustomMovieFolderFormat')}</FormLabel>

              <FormInputGroup
                inputClassName={styles.namingInput}
                type={inputTypes.TEXT}
                name="customMovieFolderFormat"
                buttons={
                  <FormInputButton
                    onPress={onCustomMovieFolderNamingModalOpenClick}
                    isDisabled={libraryStructure !== 'custom'}
                  >
                    ?
                  </FormInputButton>
                }
                onChange={handleInputChange}
                {...settings.customMovieFolderFormat}
                isDisabled={libraryStructure !== 'custom'}
                helpTexts={[
                  translate('CustomMovieFolderFormatHelpText'),
                  ...customMovieFolderFormatHelpTexts,
                ]}
                errors={[
                  ...customMovieFolderFormatErrors,
                  ...settings.customMovieFolderFormat.errors,
                ]}
              />
            </FormGroup>
          ) : null}

          {namingModalOptions ? (
            <NamingModal
              isOpen={isNamingModalOpen}
              {...namingModalOptions}
              value={settings[namingModalOptions.name].value}
              onInputChange={handleInputChange}
              onModalClose={setNamingModalClosed}
            />
          ) : null}
        </Form>
      ) : null}
    </FieldSet>
  );
}

export default Naming;
